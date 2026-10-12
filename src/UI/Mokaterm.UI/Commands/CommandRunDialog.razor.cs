using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.Connections;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Presentation;
using Mokaterm.UI.Shell;

namespace Mokaterm.UI.Commands;

/// <summary>
/// Runs one command on several saved logins and shows every host's result as it lands. The dialog writes the command,
/// picks the logins and sets the limits, then follows one <see cref="ICommandRun"/> until it is closed, which disposes
/// the run and with it every connection still open. The command is kept nowhere unless the user saves it as a command.
/// </summary>
public sealed partial class CommandRunDialog : ShellEditorBase<CommandRunEditorRequest>
{
	private static readonly IReadOnlyList<EnumOption<LoginListView>> ListViews =
	[
		new(LoginListView.All, "All logins"),
		new(LoginListView.Selected, "Picked"),
	];

	private readonly HashSet<Guid> _selected = [];
	private readonly HashSet<Guid> _collapsed = [];
	private List<LoginRow> _logins = [];
	private IReadOnlyList<CommandSnippet> _snippets = [];
	private CommandRunSettings _options = new();
	private string _command = "";
	private string _filter = "";
	private LoginListView _listView = LoginListView.All;
	private string? _error;
	private bool _starting;
	private bool _retrying;
	private bool _stopRequested;
	private bool _subscribed;

	private ICommandRun? _run;
	private IReadOnlyList<CommandHostResult> _results = [];
	private List<LoginRow> _ranOn = [];
	private ResultView _resultView = ResultView.All;
	private int _renderQueued;

	private enum LoginListView
	{
		All,
		Selected,
	}

	private enum ResultView
	{
		All,
		Problems,
	}

	[Inject]
	private CatalogState Catalog { get; set; } = default!;

	[Inject]
	private ICommandRunner Runner { get; set; } = default!;

	[Inject]
	private ICommandSnippetStore Snippets { get; set; } = default!;

	[Inject]
	private CommandSnippetActions SnippetActions { get; set; } = default!;

	[Inject]
	private ISettingsService Settings { get; set; } = default!;

	[Inject]
	private IProtocolRegistry Protocols { get; set; } = default!;

	[Inject]
	private ILogger<CommandRunDialog> Logger { get; set; } = default!;

	/// <summary>
	/// While hosts are still running the dialog cannot be closed by accident; Stop is the way out, and once it was
	/// pressed the dialog closes even if a host is still letting go, since closing finishes that in the background.
	/// </summary>
	protected override bool IsBusy => _starting || (_run is { IsRunning: true } && !_stopRequested);

	private bool IsRunning => _run is { IsRunning: true };

	private IReadOnlyList<LoginRow> VisibleLogins
	{
		get
		{
			string filter = _filter.Trim();
			return
			[
				.. _logins.Where(row =>
					(_listView == LoginListView.All || _selected.Contains(row.Login.Id))
					&& (filter.Length == 0 || row.SearchText.Contains(filter, StringComparison.CurrentCultureIgnoreCase))),
			];
		}
	}

	/// <summary>The picked logins that can run a command, in list order: what Run sends.</summary>
	private List<LoginRow> PickedRows => [.. _logins.Where(row => row.Unavailable is null && _selected.Contains(row.Login.Id))];

	private string? CommandProblem => RemoteCommandRequest.Normalize(_command).Length == 0 ? null : RemoteCommandRequest.Problem(_command);

	private bool CanRun => !_starting && RemoteCommandRequest.Problem(_command) is null && PickedRows.Count > 0;

	private string RunLabel => PickedRows.Count switch
	{
		0 => "Run",
		1 => "Run on 1 login",
		int count => string.Create(CultureInfo.CurrentCulture, $"Run on {count} logins"),
	};

	private string SelectionSummary
	{
		get
		{
			int runnable = _logins.Count(row => row.Unavailable is null);
			return string.Create(CultureInfo.CurrentCulture, $"{PickedRows.Count} of {runnable} picked");
		}
	}

	/// <summary>The command as the run sent it, or as it is typed before a run.</summary>
	private string CurrentCommand => _run?.Command ?? RemoteCommandRequest.Normalize(_command);

	private List<LoginRow> SaveTargets => _run is null ? PickedRows : _ranOn;

	/// <summary>A saved command is one line, so a script cannot be saved, and saving needs something to save.</summary>
	private bool CanSaveCommand =>
		RemoteCommandRequest.Problem(CurrentCommand) is null
		&& !CurrentCommand.AsSpan().ContainsAny('\r', '\n')
		&& SaveTargets.Count > 0;

	private string SaveHint => CurrentCommand.AsSpan().ContainsAny('\r', '\n')
		? "A saved command is a single line, so a script cannot be saved."
		: "Keep this command with the saved commands. Nothing else keeps it.";

	private IReadOnlyList<CommandHostResult> VisibleResults =>
		_resultView == ResultView.All ? _results : [.. _results.Where(host => host.IsProblem)];

	private IReadOnlyList<EnumOption<ResultView>> ResultViews =>
	[
		new(ResultView.All, string.Create(CultureInfo.CurrentCulture, $"All ({_results.Count})")),
		new(ResultView.Problems, string.Create(CultureInfo.CurrentCulture, $"Failed or non-zero ({_results.Count(host => host.IsProblem)})")),
	];

	private int RetryableCount => _results.Count(host => host.State is HostRunState.Failed or HostRunState.Cancelled);

	private int SkippedCount => _results.Count(host => host.State == HostRunState.Skipped);

	private bool AllCollapsed => _results.Count > 0 && _results.All(host => _collapsed.Contains(host.ConnectionId));

	protected override async Task OnInitializedAsync()
	{
		Catalog.Changed += OnCatalogChanged;
		_subscribed = true;
		await base.OnInitializedAsync();
	}

	protected override async Task LoadAsync(CommandRunEditorRequest request)
	{
		_options = Settings.Get<CommandRunSettings>().Clamped();
		await Catalog.EnsureLoadedAsync();
		if (!IsCurrent(request))
		{
			return;
		}

		BuildLogins();
		IEnumerable<Guid> picked = request.ConnectionIds;
		if (request.Snippet is { } snippet)
		{
			_command = snippet.Command;
			if (request.ConnectionIds.Count == 0)
			{
				picked = CommandRunTargets.ForSnippet(Catalog.Catalog, snippet).Select(login => login.Id);
			}
		}

		foreach (Guid id in picked)
		{
			if (_logins.Any(row => row.Login.Id == id && row.Unavailable is null))
			{
				_ = _selected.Add(id);
			}
		}

		// A run opened from the tree or a saved command starts on what it was opened with; the full list is one click away.
		_listView = _selected.Count > 0 ? LoginListView.Selected : LoginListView.All;
		await LoadSnippetsAsync(request);
	}

	protected override void Reset()
	{
		ReleaseRun();

		// The command can carry a token, so nothing of it outlives the request.
		_command = "";
		_filter = "";
		_error = null;
		_selected.Clear();
		_collapsed.Clear();
		_logins = [];
		_ranOn = [];
		_snippets = [];
		_results = [];
		_listView = LoginListView.All;
		_resultView = ResultView.All;
		_starting = false;
		_retrying = false;
		_stopRequested = false;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && _subscribed)
		{
			_subscribed = false;
			Catalog.Changed -= OnCatalogChanged;
		}

		base.Dispose(disposing);
	}

	private static string SnippetLabel(CommandSnippet snippet) =>
		string.Equals(snippet.Name, snippet.Command, StringComparison.Ordinal) ? snippet.Command : $"{snippet.Name}: {snippet.Command}";

	private string RowClass(LoginRow row) => row switch
	{
		{ Unavailable: not null } => "mt-run-login mt-run-login--off",
		_ when _selected.Contains(row.Login.Id) => "mt-run-login mt-run-login--on",
		_ => "mt-run-login",
	};

	private void BuildLogins()
	{
		ConnectionCatalog catalog = Catalog.Catalog;
		List<LoginRow> rows = [];
		foreach (HostProfile host in catalog.Hosts.OrderBy(host => host.DisplayName, StringComparer.CurrentCultureIgnoreCase))
		{
			foreach (ConnectionProfile login in catalog.ConnectionsOf(host.Id))
			{
				string protocol = Protocols.DisplayName(login.ProtocolId);
				string meta = string.Equals(host.DisplayName, host.Address, StringComparison.Ordinal) ? protocol : $"{protocol} on {host.DisplayName}";
				string search = string.Join(' ', [login.GetTitle(host), host.DisplayName, host.Address, login.Username ?? "", protocol, .. host.Tags]);
				rows.Add(new LoginRow(login, host, login.GetTitle(host), meta, CommandRunTargets.Unavailable(login, Protocols), search));
			}
		}

		_logins = rows;

		// A login deleted while the dialog is open leaves the selection with it.
		_selected.IntersectWith(rows.Where(row => row.Unavailable is null).Select(row => row.Login.Id));
	}

	private async Task LoadSnippetsAsync(CommandRunEditorRequest request)
	{
		try
		{
			IReadOnlyList<CommandSnippet> snippets = await Snippets.ListAsync();
			if (IsCurrent(request))
			{
				_snippets = snippets;
			}
		}
		catch (VaultLockedException)
		{
			// The shell is about to go away with the vault.
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// The picker is a shortcut; typing the command still works without it.
			Logger.LogWarning("Loading the saved commands for a command run failed: {Failure}", LogSafe.Describe(ex));
		}
	}

	private void OnCatalogChanged() => _ = InvokeAsync(() =>
	{
		if (Request is null)
		{
			return;
		}

		BuildLogins();
		StateHasChanged();
	});

	private void OnCommandChanged(string? command) => _command = command ?? "";

	private async Task OnCommandKeyDownAsync(KeyboardEventArgs args)
	{
		// Enter belongs to the script; Ctrl+Enter runs it, the way the saved commands panel runs a command.
		if (args.Key == "Enter" && (args.CtrlKey || args.MetaKey))
		{
			await RunAsync();
		}
	}

	private void PickSnippet(CommandSnippet? snippet)
	{
		if (snippet is null)
		{
			return;
		}

		_command = snippet.Command;
		if (_selected.Count == 0)
		{
			foreach (Guid id in CommandRunTargets.Runnable(CommandRunTargets.ForSnippet(Catalog.Catalog, snippet), Protocols))
			{
				_ = _selected.Add(id);
			}
		}
	}

	private void OnFilterChanged(string filter) => _filter = filter;

	private void SetListView(LoginListView view) => _listView = view;

	private void Toggle(LoginRow row)
	{
		if (row.Unavailable is not null || _starting)
		{
			return;
		}

		if (!_selected.Remove(row.Login.Id))
		{
			_ = _selected.Add(row.Login.Id);
		}
	}

	private void OnLoginKeyDown(KeyboardEventArgs args, LoginRow row)
	{
		if (args.Key is " " or "Enter")
		{
			Toggle(row);
		}
	}

	private void PickShown()
	{
		foreach (LoginRow row in VisibleLogins.Where(row => row.Unavailable is null))
		{
			_ = _selected.Add(row.Login.Id);
		}
	}

	private void ClearPicked() => _selected.Clear();

	private Task SetParallelAsync(int value) => SaveOptionsAsync(_options with { ParallelHosts = value });

	private Task SetTimeoutAsync(int value) => SaveOptionsAsync(_options with { TimeoutSeconds = value });

	private Task SetOutputAsync(int value) => SaveOptionsAsync(_options with { OutputKilobytes = value });

	private Task SetSkipAsync(bool value) => SaveOptionsAsync(_options with { SkipHostsThatAsk = value });

	/// <summary>The limits are remembered for the next run; they say nothing about the command, which is never stored here.</summary>
	private async Task SaveOptionsAsync(CommandRunSettings options)
	{
		_options = options.Clamped();
		CommandRunSettings saved = _options;
		try
		{
			await Settings.UpdateAsync<CommandRunSettings>(_ => saved);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// The run uses what is on screen either way; only the next one would start from the old values.
			Logger.LogWarning("Saving the command run settings failed: {Failure}", LogSafe.Describe(ex));
		}
	}

	private async Task RunAsync()
	{
		if (!CanRun || Request is not { } request)
		{
			return;
		}

		List<LoginRow> targets = PickedRows;
		_starting = true;
		_error = null;
		try
		{
			ICommandRun run = await Runner.StartAsync(new CommandRunRequest
			{
				Command = _command,
				ConnectionIds = [.. targets.Select(row => row.Login.Id)],
				Options = _options,
			});

			if (!IsCurrent(request))
			{
				_ = run.DisposeAsync().AsTask();
				return;
			}

			AttachRun(run, targets);
		}
		catch (ArgumentException ex)
		{
			_error = ex.Message;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError("Starting a command run failed: {Failure}", LogSafe.Describe(ex));
			_error = VaultFailure.Describe(ex, "run the command");
		}
		finally
		{
			_starting = false;
		}
	}

	private void AttachRun(ICommandRun run, List<LoginRow> targets)
	{
		ReleaseRun();
		_run = run;
		_ranOn = targets;
		_results = run.Hosts;
		_collapsed.Clear();
		_resultView = ResultView.All;
		_stopRequested = false;
		run.Changed += OnRunChanged;
	}

	/// <summary>Lets go of the run on screen. Disposing it cancels what is still going and closes every connection it holds.</summary>
	private void ReleaseRun()
	{
		if (_run is not { } run)
		{
			return;
		}

		_run = null;
		run.Changed -= OnRunChanged;
		_ = run.DisposeAsync().AsTask();
	}

	/// <summary>Coalesces a burst of host changes into one render: forty hosts finishing together is one redraw, not forty.</summary>
	private void OnRunChanged()
	{
		if (Interlocked.Exchange(ref _renderQueued, 1) == 1)
		{
			return;
		}

		_ = InvokeAsync(() =>
		{
			Volatile.Write(ref _renderQueued, 0);
			if (_run is { } run)
			{
				_results = run.Hosts;
				StateHasChanged();
			}
		});
	}

	private void Stop()
	{
		_stopRequested = true;
		_run?.CancelAll();
	}

	private Task RetryFailedAsync() => RetryAsync(static run => run.RetryFailedAsync());

	private Task RetrySkippedAsync() => RetryAsync(static run => run.RetrySkippedAsync());

	private async Task RetryAsync(Func<ICommandRun, Task<int>> retry)
	{
		if (_run is not { } run || _retrying)
		{
			return;
		}

		_retrying = true;
		_error = null;
		_stopRequested = false;
		try
		{
			_ = await retry(run);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Logger.LogError("Retrying hosts of a command run failed: {Failure}", LogSafe.Describe(ex));
			_error = VaultFailure.Describe(ex, "run the command again");
		}
		finally
		{
			_retrying = false;
		}
	}

	/// <summary>Back to the command and the logins, as they were, for a changed command or another set of hosts.</summary>
	private void EditAgain()
	{
		if (_run is { } run)
		{
			_command = run.Command;
			_selected.Clear();
			_selected.UnionWith(_ranOn.Select(row => row.Login.Id));
		}

		ReleaseRun();
		_results = [];
		_ranOn = [];
		_error = null;
		_stopRequested = false;
	}

	private Task SaveCommandAsync() =>
		CanSaveCommand
			? SnippetActions.SaveFromRunAsync(CurrentCommand, [.. SaveTargets.Select(row => (row.Host, row.Login))])
			: Task.CompletedTask;

	private void SetResultView(ResultView view) => _resultView = view;

	private void ToggleHost(Guid connectionId)
	{
		if (!_collapsed.Remove(connectionId))
		{
			_ = _collapsed.Add(connectionId);
		}
	}

	private void ToggleAllOutput()
	{
		if (AllCollapsed)
		{
			_collapsed.Clear();
		}
		else
		{
			_collapsed.UnionWith(_results.Select(host => host.ConnectionId));
		}
	}

	private string ResultsSummary()
	{
		int done = _results.Count(host => host.State == HostRunState.Done && host.ExitCode == 0);
		int nonZero = _results.Count(host => host.State == HostRunState.Done && host.ExitCode != 0);
		int failed = _results.Count(host => host.State == HostRunState.Failed);
		int skipped = SkippedCount;
		int cancelled = _results.Count(host => host.State == HostRunState.Cancelled);
		int pending = _results.Count(host => host.IsPending);
		List<string> parts = [];
		Add(done, "succeeded");
		Add(nonZero, "non-zero");
		Add(failed, "failed");
		Add(skipped, "skipped");
		Add(cancelled, "cancelled");
		Add(pending, "to go");
		return string.Join(", ", parts);

		void Add(int count, string what)
		{
			if (count > 0)
			{
				parts.Add(string.Create(CultureInfo.CurrentCulture, $"{count} {what}"));
			}
		}
	}

	/// <summary>One saved login as the picker lists it. <see cref="Unavailable"/> says why it cannot be picked.</summary>
	private sealed record LoginRow(ConnectionProfile Login, HostProfile Host, string Title, string Meta, string? Unavailable, string SearchText);
}
