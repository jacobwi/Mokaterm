using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Moka.Red.Core.Enums;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.ServerAdmin;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Detection;
using Mokaterm.Modules.Ssh.Admin.Monitoring;
using Mokaterm.Modules.Ssh.Admin.Reboot;
using Mokaterm.Modules.Ssh.FileSystem;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Editing;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.Common.ToolPages;

namespace Mokaterm.Modules.Ssh.Components.Admin;

/// <summary>
/// The Linux system view of a session: what the server is in a strip at the top, a page per area, a review panel
/// that shows the exact commands of a change before it runs, and the last change's result at the bottom.
/// </summary>
/// <remarks>
/// It opens as a session tool, which is the only surface the shell gives a module beside a terminal. The pages are the
/// registered <see cref="SystemPageDescriptor"/>s, this module's and any other's, opened by id. The navigation shows
/// labels when the view is wide, icons when it is not, and a dropdown when it is narrow; each page lays out its list
/// and detail panel side by side or one above the other by its own width.
/// </remarks>
public sealed partial class SystemAdminView : SessionViewBase, IAsyncDisposable
{
	/// <summary>
	/// The id this view registers under as a session tool. A session's live figures name it, so a click on them in the
	/// status bar opens this view.
	/// </summary>
	public const string ToolId = "system";

	private readonly CancellationTokenSource _closed = new();
	private ISessionHandle? _handle;
	private IProtocolSession? _attached;
	private SystemAdminFeature? _feature;
	private SystemAdminFeature? _ownedFeature;
	private SessionRootFiles? _ownedRootFiles;
	private HostMonitor? _monitor;
	private HostMonitor? _ownedMonitor;
	private Task<SessionLogin>? _login;
	private SystemProfile? _profile;
	private AdminPageContext? _context;
	private string _page = AdminPageIds.Overview;
	private IReadOnlyCollection<string>? _probedCommands;
	private bool _detecting;
	private string? _detectError;
	private bool _rootBusy;
	private string? _rootError;

	private AdminActionResult? _shownResult;
	private bool _outputOpen;

	private AdminAction? _review;
	private AdminInput? _reviewInput;
	private TaskCompletionSource<bool>? _reviewAnswer;
	private ElementReference _reviewDefault;
	private bool _focusReview;

	[Inject]
	private TimeProvider Time { get; set; } = default!;

	[Inject]
	private ILogger<SystemAdminView> Logger { get; set; } = default!;

	[Inject]
	private RebootReconnects Reboots { get; set; } = default!;

	[Inject]
	private ICredentialStore Credentials { get; set; } = default!;

	[Inject]
	private IUiContributions Contributions { get; set; } = default!;

	[Inject]
	private IRemoteTextEditor Editor { get; set; } = default!;

	private bool IsBusy => _review is not null || _feature?.Running is not null;

	private AdminActionResult? Result => _feature?.Running ?? _feature?.Last;

	/// <summary>The page on show: the one picked, or the first when no page has that id (one a plugin took with it).</summary>
	private SystemPageDescriptor? CurrentPage =>
		Contributions.SystemPages.FirstOrDefault(page => page.Id == _page) ?? (Contributions.SystemPages is [var first, ..] ? first : null);

	/// <summary>The commands registered pages ask about, which detection looks for along with its own.</summary>
	private IReadOnlyCollection<string> ProbedCommands =>
		_probedCommands ??= [.. Contributions.SystemPages.SelectMany(page => page.Commands).Distinct(StringComparer.Ordinal)];

	public async ValueTask DisposeAsync()
	{
		if (_handle is { } handle)
		{
			handle.Changed -= OnSessionChanged;
			_handle = null;
		}

		Reboots.Changed -= OnRebootChanged;

		_reviewAnswer?.TrySetResult(false);
		await DetachAsync();
		await _closed.CancelAsync();
		_closed.Dispose();
	}

	protected override void OnInitialized() => Reboots.Changed += OnRebootChanged;

	protected override async Task OnParametersSetAsync()
	{
		if (!ReferenceEquals(_handle, Session))
		{
			if (_handle is { } previous)
			{
				previous.Changed -= OnSessionChanged;
			}

			_handle = Session;
			_handle.Changed += OnSessionChanged;
		}

		await AttachAsync();
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (_focusReview)
		{
			_focusReview = false;
			try
			{
				await _reviewDefault.FocusAsync();
			}
			catch (Exception ex) when (ex is InvalidOperationException or Microsoft.JSInterop.JSException or TaskCanceledException)
			{
				// The panel closed before the browser got there.
			}
		}
	}

	/// <summary>Init system, package manager, firewall and container, in the strip's order.</summary>
	private static IEnumerable<string> SystemParts(SystemProfile profile)
	{
		yield return profile.Init switch
		{
			InitSystem.Systemd => "systemd",
			InitSystem.OpenRc => "OpenRC",
			InitSystem.SysV => "sysvinit",
			InitSystem.BusyBox => "BusyBox init",
			_ => "no init",
		};

		if (profile.Capabilities.Packages is { } packages)
		{
			yield return packages.DisplayName;
		}

		if (profile.Capabilities.Firewall is { } firewall)
		{
			yield return firewall.DisplayName;
		}

		if (profile.IsContainer)
		{
			yield return "container";
		}
	}

	/// <summary>Every page, with the reason the detected system cannot use it. Such pages stay in the list, greyed.</summary>
	private List<ToolNavEntry> NavEntries(SystemProfile profile)
	{
		if (_context is not { } context || !ReferenceEquals(context.Profile, profile))
		{
			return [.. Contributions.SystemPages.Select(page => new ToolNavEntry(page.Id, page.Title, page.Icon))];
		}

		return
		[
			.. Contributions.SystemPages.Select(page => new ToolNavEntry(
				page.Id,
				page.Title,
				page.Icon,
				ToolPageRules.Evaluate(page.Unavailable, (ISystemPageContext)context, Logger, page.Id))),
		];
	}

	private static MokaColor ResultColor(AdminActionResult result) =>
		result.IsRunning ? MokaColor.Warning : result.Succeeded ? MokaColor.Success : MokaColor.Error;

	private static string ResultClass(AdminActionResult result) =>
		result.IsRunning ? "mta-result mta-result--running" : result.Succeeded ? "mta-result" : "mta-result mta-result--failed";

	private static string ResultLabel(AdminActionResult result) => result switch
	{
		{ IsRunning: true } => "RUNNING",
		{ Failure: not null } => "DID NOT FINISH",
		{ Succeeded: true } => "OK",
		_ => "FAILED",
	};

	/// <summary>Exit code and duration in one mono run, such as <c>exit 0 · 1.2 s</c>.</summary>
	private static string ResultMeta(AdminActionResult result)
	{
		if (result.Outcome is not { } outcome)
		{
			return "";
		}

		string exit = outcome.ExitCode is { } code
			? string.Create(CultureInfo.InvariantCulture, $"exit {code}")
			: $"signal {outcome.ExitSignal ?? "?"}";
		string duration = outcome.Duration.TotalSeconds < 1
			? string.Create(CultureInfo.InvariantCulture, $"{outcome.Duration.TotalMilliseconds:0} ms")
			: outcome.Duration.TotalMinutes < 1
				? string.Create(CultureInfo.InvariantCulture, $"{outcome.Duration.TotalSeconds:0.0} s")
				: DisplayFormat.Duration(outcome.Duration);
		return $"{exit} · {duration}";
	}

	private static string ResultText(AdminActionResult result)
	{
		if (result.Failure is { } failure)
		{
			return failure;
		}

		if (result.Outcome is not { } outcome)
		{
			return "";
		}

		string output = TerminalTextFilter.Plain(outcome.Output).TrimEnd();
		string error = TerminalTextFilter.Plain(outcome.Error).TrimEnd();
		string text = (output, error) switch
		{
			({ Length: > 0 }, { Length: > 0 }) => output + "\n\n" + error,
			({ Length: > 0 }, _) => output,
			(_, { Length: > 0 }) => error,
			_ => "(no output)",
		};

		return outcome.IsTruncated ? text + "\n\n(the output was longer; only its start is kept)" : text;
	}

	/// <summary>A new result opens its output when it failed and keeps it folded when it went through.</summary>
	private bool OutputOpen(AdminActionResult result)
	{
		if (!ReferenceEquals(result, _shownResult))
		{
			_shownResult = result;
			_outputOpen = !result.IsRunning && !result.Succeeded;
		}

		return _outputOpen;
	}

	private string RunsAs(AdminAction action)
	{
		if (!action.Script.AsRoot)
		{
			return $"Runs as {_profile?.Facts.UserName}, without sudo";
		}

		if (_feature?.LoginIsRoot == true)
		{
			return "Runs as root, the login itself";
		}

		return _feature?.RootModeOn == true ? "Runs as root through sudo" : "Runs as root through sudo, which may ask for the password";
	}

	private async Task AttachAsync()
	{
		IProtocolSession? session = Session.Session;
		if (ReferenceEquals(session, _attached))
		{
			return;
		}

		await DetachAsync();
		_attached = session;
		if (session is null)
		{
			return;
		}

		_feature = session.GetFeature<SystemAdminFeature>();
		if (_feature is null && session.GetFeature<IRemoteCommandFeature>() is { } commands)
		{
			// A session that can run commands but is not SSH (the DevHost's demo): reads only, owned by this view, with
			// the session's files as root where it has them.
			_ownedRootFiles = session.GetFeature<IFileSystemFeature>() is { } files ? new SessionRootFiles(files) : null;
			_ownedFeature = new SystemAdminFeature(
				new CommandFeatureAdminShell(commands, Session.Connection.GetTitle(Session.Host), Session.Connection.AuthenticationMethod),
				Time,
				Logger,
				_ownedRootFiles);
			_feature = _ownedFeature;
		}

		if (_feature is { } feature)
		{
			// The session's own sampler when it has one, which the status bar reads too; otherwise one of the view's own.
			_monitor = session.GetFeature<IHostMonitorFeature>() as HostMonitor;
			if (_monitor is null)
			{
				_ownedMonitor = new HostMonitor(feature.ReadAsync, Time, Logger, toolKey: null);
				_monitor = _ownedMonitor;
			}

			feature.Changed += OnFeatureChanged;
			_profile = feature.Profile;
			BuildContext();
			await DetectAsync(refresh: false);
		}
	}

	private async Task DetachAsync()
	{
		if (_feature is { } feature)
		{
			feature.Changed -= OnFeatureChanged;

			// Root mode lasts as long as the view the user can see; a running change keeps sudo until it finishes.
			if (!ReferenceEquals(feature, _ownedFeature))
			{
				feature.DisableRoot();
			}
		}

		// Before the feature it reads through.
		if (_ownedMonitor is { } monitor)
		{
			_ownedMonitor = null;
			await monitor.DisposeAsync();
		}

		_monitor = null;
		if (_ownedFeature is { } owned)
		{
			_ownedFeature = null;
			await owned.DisposeAsync();
		}

		if (_ownedRootFiles is { } rootFiles)
		{
			_ownedRootFiles = null;
			await rootFiles.DisposeAsync();
		}

		_login = null;
		_feature = null;
		_attached = null;
		_profile = null;
		_context = null;
	}

	private async Task DetectAsync(bool refresh)
	{
		if (_feature is not { } feature)
		{
			return;
		}

		_detecting = true;
		_detectError = null;
		StateHasChanged();
		try
		{
			_profile = await feature.DetectAsync(refresh, ProbedCommands, _closed.Token);
			BuildContext();
		}
		catch (AdminException ex)
		{
			_detectError = ex.Message;
		}
		catch (OperationCanceledException) when (_closed.IsCancellationRequested)
		{
			return;
		}
		finally
		{
			_detecting = false;
		}
	}

	private void BuildContext()
	{
		if (_feature is not { } feature || _profile is not { } profile || _monitor is not { } monitor)
		{
			_context = null;
			return;
		}

		if (_context is not null && ReferenceEquals(_context.Profile, profile) && ReferenceEquals(_context.Feature, feature))
		{
			return;
		}

		_context = new AdminPageContext
		{
			Feature = feature,
			Profile = profile,
			Monitor = monitor,
			Run = ReviewAndRunAsync,
			EnableRoot = EnableRootAsync,
			Show = Show,
			SessionAddress = Session.Host.Address,
			IsBusy = () => IsBusy,
			IsVisible = () => IsActive,
			Reboot = RebootAsync,
			Login = () => _login ??= ResolveLoginAsync(feature, profile),
			Editor = Editor,
		};
	}

	/// <summary>
	/// How this session logged in: the method from the session itself, and for a key login its fingerprint from the
	/// keychain or the agent identity the connection names. A locked vault leaves the fingerprint unknown.
	/// </summary>
	private async Task<SessionLogin> ResolveLoginAsync(SystemAdminFeature feature, SystemProfile profile)
	{
		AuthenticationMethod? method = feature.LoginMethod;
		string? fingerprint = null;
		if (method == AuthenticationMethod.PublicKey && Session.Connection.CredentialId is { } credentialId)
		{
			try
			{
				fingerprint = (await Credentials.FindAsync(credentialId, _closed.Token))?.KeyFingerprint;
			}
			catch (Exception ex) when (ex is VaultLockedException or OperationCanceledException or ObjectDisposedException)
			{
				// Unknown, which every warning that depends on it allows for.
			}
		}
		else if (method == AuthenticationMethod.Agent)
		{
			fingerprint = SshConnectionOptions.FromOptions(Session.Connection.Options).AgentIdentity;
		}

		return new SessionLogin(profile.Facts.UserName, method, fingerprint);
	}

	/// <summary>
	/// Reviews and runs the reboot. Once it was sent (or the connection went while it ran), the window waits for the
	/// session to drop and reconnects it when the server answers again, whether or not this view is still open.
	/// </summary>
	private async Task RebootAsync()
	{
		if (_profile is not { } profile || !RebootCommands.CanReboot(profile))
		{
			return;
		}

		ISessionHandle session = Session;
		AdminActionResult? result = await ReviewAndRunAsync(RebootCommands.Reboot(profile), null);

		// A reboot that did not finish either took the connection with it, or never ran (sudo refused): only the
		// first has anything to reconnect.
		if (result is { Succeeded: true } || (result is { Failure: not null } && await DropsAsync(session)))
		{
			Reboots.Start(session);
		}
	}

	/// <summary>Whether <paramref name="session"/> leaves Connected within a few seconds.</summary>
	private async Task<bool> DropsAsync(ISessionHandle session)
	{
		for (int look = 0; look < 20 && session.State == SessionState.Connected; look++)
		{
			await Task.Delay(TimeSpan.FromMilliseconds(250), Time);
		}

		return session.State != SessionState.Connected;
	}

	private void StopRebootWatch() => Reboots.Stop(Session.Id);

	private string RebootTitle(RebootWatch watch) => watch.State switch
	{
		RebootWatchState.WaitingForDown => $"Rebooting {Session.Host.DisplayName}",
		RebootWatchState.Reconnecting => $"Reconnecting to {Session.Host.DisplayName}",
		_ => $"Waiting for {Session.Host.DisplayName} to come back",
	};

	private static string RebootText(RebootWatch watch) => watch.State switch
	{
		RebootWatchState.WaitingForDown => "The reboot was sent. Waiting for the connection to go.",
		RebootWatchState.Reconnecting => "The server answers again. Logging in.",
		_ when watch.Attempts > 0 => string.Create(CultureInfo.CurrentCulture, $"The server did not let the session in yet ({watch.Attempts} tries). Trying again shortly, until {watch.StartedAt.ToLocalTime() + watch.Times.Down + watch.Times.Back:HH:mm}."),
		_ => "The session reconnects as soon as the server answers SSH again.",
	};

	private void OnRebootChanged() => _ = InvokeAsync(StateHasChanged);

	private void Show(string page)
	{
		_page = page;
		_ = InvokeAsync(StateHasChanged);
	}

	private Task RefreshDetectionAsync() => DetectAsync(refresh: true);

	private async Task EnableRootAsync()
	{
		if (_feature is not { } feature || _rootBusy)
		{
			return;
		}

		_rootBusy = true;
		_rootError = null;
		try
		{
			await feature.EnableRootAsync(_closed.Token);
		}
		catch (AdminException ex)
		{
			_rootError = ex.Message;
		}
		catch (OperationCanceledException) when (_closed.IsCancellationRequested)
		{
		}
		finally
		{
			_rootBusy = false;
		}
	}

	private void DisableRoot()
	{
		_rootError = null;
		_feature?.DisableRoot();
	}

	/// <summary>Shows the review panel and waits for Run or Cancel; on Run, runs the action for the session.</summary>
	private async Task<AdminActionResult?> ReviewAndRunAsync(AdminAction action, AdminInput? input)
	{
		if (_feature is not { } feature || _review is not null)
		{
			return null;
		}

		TaskCompletionSource<bool> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_review = action;
		_reviewInput = input;
		_reviewAnswer = answer;
		_focusReview = true;
		StateHasChanged();

		bool confirmed;
		try
		{
			confirmed = await answer.Task;
		}
		finally
		{
			_review = null;
			_reviewInput = null;
			_reviewAnswer = null;
		}

		if (!confirmed)
		{
			StateHasChanged();
			return null;
		}

		try
		{
			return await feature.RunAsync(action, input);
		}
		catch (AdminException ex)
		{
			_rootError = ex.Message;
			return null;
		}
	}

	private void AnswerReview(bool run) => _reviewAnswer?.TrySetResult(run);

	private void OnReviewKeyDown(KeyboardEventArgs args)
	{
		if (args.Key == "Escape")
		{
			AnswerReview(false);
		}
	}

	private void ToggleOutput() => _outputOpen = !_outputOpen;

	private void OnFeatureChanged() => _ = InvokeAsync(() =>
	{
		if (_feature is { } feature && feature.Profile is { } profile && !ReferenceEquals(profile, _profile) && !_detecting)
		{
			_profile = profile;
			BuildContext();
		}

		StateHasChanged();
	});

	private void OnSessionChanged() => _ = InvokeAsync(async () =>
	{
		await AttachAsync();
		StateHasChanged();
	});
}
