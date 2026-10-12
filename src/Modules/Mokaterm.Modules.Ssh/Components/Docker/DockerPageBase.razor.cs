using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Tools;
using Mokaterm.UI.Common.Files;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// What every page of the Docker manager shares: the session's Docker client, a load with its spinner and its error,
/// changes that are confirmed with the exact command and leave their outcome on screen, and polling that only runs while
/// the page is visible. The members that touch the module's own types are <c>private protected</c>, so the pages can
/// be public components without making those types public.
/// </summary>
public abstract partial class DockerPageBase : ComponentBase, IDisposable
{
	/// <summary>How many lines of a change's output the folded box keeps: the end, which is where the outcome is.</summary>
	private const int ChangeTailLines = 60;

	private readonly CancellationTokenSource _lifetime = new();
	private PagePoller? _poller;
	private CancellationTokenSource? _change;
	private ILogger? _logger;
	private bool _loading;
	private bool _disposed;
	private bool? _changeOutputShown;

	[Parameter, EditorRequired]
	public ISessionHandle Session { get; set; } = default!;

	/// <summary>False while the session's tab is hidden: polling waits instead of running commands nobody sees.</summary>
	[Parameter]
	public bool Visible { get; set; } = true;

	/// <summary>Opens another page of the manager; null outside one, such as in a test.</summary>
	[CascadingParameter]
	private protected DockerNavigation? Navigation { get; set; }

	[Inject]
	private protected IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private protected TimeProvider Time { get; set; } = default!;

	[Inject]
	private ILoggerFactory LoggerFactory { get; set; } = default!;

	[Inject]
	private ISessionManager Sessions { get; set; } = default!;

	[Inject]
	private ISessionTabs Tabs { get; set; } = default!;

	[Inject]
	private IFileBrowserDialog FileBrowser { get; set; } = default!;

	private protected DockerSessionFeature? Feature => Session.Session?.GetFeature<DockerSessionFeature>();

	/// <summary>Whether "Open shell" can open a tab for this login: its protocol has to bring a terminal.</summary>
	private protected bool CanOpenShellTab => Session.Protocol.Has(ProtocolCapabilities.Terminal);

	private protected DockerClient? Client => Feature?.Client;

	/// <summary>The docker host's files through the session's own file system, or null for a session that has none.</summary>
	private protected DockerHostFiles? HostFiles => Feature?.HostFiles;

	/// <summary>False when the connection does not run anything as root, so no page offers it.</summary>
	private protected bool CanUseRoot => HostFiles?.CanUseRoot ?? false;

	/// <summary>Cancelled when the page goes away.</summary>
	private protected CancellationToken Lifetime => _lifetime.Token;

	/// <summary>True while the first load has not finished.</summary>
	private protected bool IsFirstLoad { get; private set; } = true;

	private protected bool IsLoading => _loading;

	/// <summary>Why the last load failed, or null.</summary>
	private protected string? LoadError { get; set; }

	/// <summary>The change that is running, as its command, or null.</summary>
	private protected string? RunningCommand { get; private set; }

	/// <summary>What the running change is, for example "Restart web".</summary>
	private protected string? RunningTitle { get; private set; }

	/// <summary>The last change and how it ended, kept on screen until the next one or until dismissed.</summary>
	private protected DockerChange? LastChange { get; set; }

	private protected bool IsBusy => RunningCommand is not null;

	/// <summary>A failed change opens its output; one that went through keeps it folded until asked.</summary>
	private protected bool ChangeOutputShown => _changeOutputShown ?? LastChange is { Succeeded: false };

	private ILogger Logger => _logger ??= LoggerFactory.CreateLogger(GetType());

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	/// <summary>The last lines of a change's output, which say how it ended.</summary>
	private protected static string ChangeTail(DockerChange change)
	{
		ArgumentNullException.ThrowIfNull(change);
		string[] lines = change.Text.Split('\n');
		return lines.Length <= ChangeTailLines ? change.Text : string.Join('\n', lines[^ChangeTailLines..]);
	}

	/// <summary>Stops everything the page started. The lifetime token is cancelled but kept, since a load may still be finishing with it.</summary>
	protected virtual void Dispose(bool disposing)
	{
		if (_disposed || !disposing)
		{
			return;
		}

		_disposed = true;
		_lifetime.Cancel();
		_poller?.Dispose();
		_change?.Cancel();
	}

	/// <summary>
	/// Runs a load: one at a time, its failure kept in <see cref="LoadError"/> rather than thrown, and nothing at all once
	/// the page is gone.
	/// </summary>
	private protected async Task LoadAsync(Func<DockerClient, CancellationToken, Task> load)
	{
		ArgumentNullException.ThrowIfNull(load);
		if (_loading || _disposed || Client is not { } client)
		{
			return;
		}

		_loading = true;
		try
		{
			await load(client, Lifetime);
			LoadError = null;
		}
		catch (Exception ex) when (Describe(ex) is { } problem)
		{
			LoadError = problem;
		}
		catch (OperationCanceledException)
		{
			return;
		}
		finally
		{
			_loading = false;
			IsFirstLoad = false;
		}

		if (!_disposed)
		{
			StateHasChanged();
		}
	}

	/// <summary>
	/// Runs a change. A destructive one is confirmed first with the exact command in the question; whatever happens, its
	/// outcome stays on screen, and <paramref name="afterwards"/> (a refresh) runs once it ended.
	/// </summary>
	/// <param name="title">What it is, for example "Stop web": the confirmation's title and the outcome's heading.</param>
	/// <param name="confirm">The question for a destructive change, or null to run at once.</param>
	/// <param name="confirmText">The confirm button, for example "Stop".</param>
	private protected async Task<DockerOutput?> RunChangeAsync(
		DockerCall call,
		string title,
		string? confirm = null,
		string confirmText = "Run",
		Func<Task>? afterwards = null,
		bool destructive = true)
	{
		ArgumentNullException.ThrowIfNull(call);
		IReadOnlyList<DockerOutput>? outputs = await RunChangesAsync([call], title, confirm, confirmText, afterwards, destructive);
		return outputs is [.., var last] ? last : null;
	}

	/// <summary>
	/// Runs several calls as one change, one after the other, stopping at the first that fails: "pull first, then up".
	/// The confirmation, when there is one, shows every command.
	/// </summary>
	private protected async Task<IReadOnlyList<DockerOutput>?> RunChangesAsync(
		IReadOnlyList<DockerCall> calls,
		string title,
		string? confirm = null,
		string confirmText = "Run",
		Func<Task>? afterwards = null,
		bool destructive = true)
	{
		ArgumentNullException.ThrowIfNull(calls);
		if (IsBusy || _disposed || Client is not { } client || calls.Count == 0)
		{
			return null;
		}

		string prefix = client.IsElevated ? "sudo " : "";
		List<string> shown = [.. calls.Select(call => prefix + call.Display)];
		if (confirm is not null)
		{
			ConfirmResult answer = await Interaction.ConfirmAsync(
				new ConfirmPrompt
				{
					Title = title,
					Message = confirm,
					ConfirmText = confirmText,
					Destructive = destructive,
					Details = string.Join('\n', shown),
				},
				Lifetime);

			if (!answer.Confirmed || _disposed)
			{
				return null;
			}
		}

		// Noted for the health watch, which never reports what the user's own change did.
		using IDisposable? noted = Feature?.Changes.Begin(calls);
		using CancellationTokenSource change = CancellationTokenSource.CreateLinkedTokenSource(Lifetime);
		_change = change;
		RunningTitle = title;
		LastChange = null;
		_changeOutputShown = null;
		List<DockerOutput> outputs = [];
		try
		{
			for (int index = 0; index < calls.Count; index++)
			{
				RunningCommand = shown[index];
				StateHasChanged();
				DockerOutput output = await client.RunAsync(calls[index], change.Token);
				outputs.Add(output);
				LastChange = new DockerChange(title, string.Join(" && ", shown.Take(index + 1)), Combine(outputs), null);
				if (!output.Succeeded)
				{
					break;
				}
			}
		}
		catch (DockerCommandException ex)
		{
			LastChange = new DockerChange(title, string.Join(" && ", shown.Take(outputs.Count + 1)), ex.Output ?? (outputs.Count > 0 ? Combine(outputs) : null), ex.Message);
		}
		catch (OperationCanceledException) when (!Lifetime.IsCancellationRequested)
		{
			LastChange = new DockerChange(title, string.Join(" && ", shown.Take(outputs.Count + 1)), null, "Stopped waiting. The daemon may still finish what it started.");
		}
		catch (OperationCanceledException)
		{
			return null;
		}
		finally
		{
			_change = null;
			RunningCommand = null;
			RunningTitle = null;
		}

		StateHasChanged();
		if (afterwards is not null && !_disposed)
		{
			await afterwards();
		}

		return outputs;
	}

	/// <summary><see cref="RunChangeAsync"/> for a caller that has no use for the output beyond showing it.</summary>
	private protected async Task ChangeAsync(
		DockerCall call,
		string title,
		string? confirm = null,
		string confirmText = "Run",
		Func<Task>? afterwards = null) =>
		await RunChangeAsync(call, title, confirm, confirmText, afterwards);

	/// <summary>Runs a planned change: its call, title and question.</summary>
	private protected Task<DockerOutput?> RunPlanAsync(DockerChangePlan plan, Func<Task>? afterwards = null)
	{
		ArgumentNullException.ThrowIfNull(plan);
		return RunChangeAsync(plan.Call, plan.Title, plan.Confirm, plan.ConfirmText, afterwards, plan.Destructive);
	}

	/// <summary>Copies plain text, saying so; for names and ids, never for anything secret.</summary>
	private protected async Task CopyTextAsync(Mokaterm.UI.Common.Platform.IClipboardService clipboard, string text, string what)
	{
		ArgumentNullException.ThrowIfNull(clipboard);
		try
		{
			await clipboard.WriteTextAsync(text, Lifetime);
			Interaction.Notify(NoticeSeverity.Success, $"Copied the {what}.");
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Interaction.Notify(NoticeSeverity.Error, "Could not copy to the clipboard.");
		}
	}

	/// <summary>Stops waiting for the running change and closes its channel.</summary>
	private protected void CancelChange() => _change?.Cancel();

	private protected void DismissChange() => LastChange = null;

	private protected void ToggleChangeOutput() => _changeOutputShown = !ChangeOutputShown;

	/// <summary>
	/// Opens a new tab on the session's login that runs <c>docker exec -it NAME bash</c> (or <c>sh</c> when the image has
	/// no bash) and brings it to the front. Through sudo while the manager runs as root, which asks in that terminal.
	/// </summary>
	/// <returns>Why it could not, or null.</returns>
	private protected async Task<string?> OpenShellAsync(string name)
	{
		ArgumentNullException.ThrowIfNull(name);
		if (Client is not { } client || !CanOpenShellTab)
		{
			return "This session has no terminal to open a shell in.";
		}

		try
		{
			string shell = await client.FindShellAsync(name, Lifetime);
			(ConnectionProfile connection, SessionOpenOptions options) = DockerShellTab.For(Session.Connection, name, shell, client.IsElevated);
			ISessionHandle opened = Sessions.OpenTransient(Session.Host, connection, options);
			Tabs.Show(opened.Id);
			return null;
		}
		catch (Exception ex) when (Describe(ex) is { } problem)
		{
			return $"Could not open a shell: {problem}";
		}
	}

	/// <summary>
	/// Shows the built-in file browser over a running container's files, in this page's window, and returns once it is
	/// closed. Every operation is a docker exec as the container's own user, through sudo when the manager uses it, and
	/// a container that is stopped or has no shell says so inside the browser.
	/// </summary>
	/// <param name="startFolder">Where to open, such as the container's working directory; null for its user's home.</param>
	private protected async Task BrowseFilesAsync(string container, string? startFolder = null)
	{
		ArgumentNullException.ThrowIfNull(container);
		if (Feature is not { } feature)
		{
			Interaction.Notify(NoticeSeverity.Warning, "The session is not connected.");
			return;
		}

		IFileSystemFeature files;
		try
		{
			files = feature.ContainerFiles(container);
		}
		catch (Exception ex) when (ex is ArgumentException or ObjectDisposedException)
		{
			Interaction.Notify(NoticeSeverity.Warning, ex is ArgumentException ? ex.Message : "The session is closed.", "Cannot browse " + container);
			return;
		}

		FileBrowserRequest request = new()
		{
			FileSystem = files,
			Title = $"{container} on {feature.Host}",
			Location = container,
			StartFolder = string.IsNullOrWhiteSpace(startFolder) ? null : startFolder,
		};
		await FileBrowser.ShowAsync(request, Lifetime);
	}

	/// <summary>
	/// Calls <paramref name="tick"/> every <paramref name="interval"/> while the page is visible and nothing else is
	/// loading or running. Replaces any polling already running.
	/// </summary>
	private protected void StartPolling(TimeSpan interval, Func<Task> tick)
	{
		if (_disposed)
		{
			return;
		}

		_poller ??= new PagePoller(Time, InvokeAsync, () => Visible && !_loading && !IsBusy);
		_poller.Start(interval, tick, Lifetime);
	}

	private protected void StopPolling() => _poller?.Stop();

	/// <summary>The sentence for a failure a page shows, or null for one that is not the user's business (a cancel).</summary>
	private protected string? Describe(Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		switch (exception)
		{
			case DockerCommandException docker:
				return docker.Message;
			case RemoteFileSystemException files:
				return files.Message;
			case ArgumentException argument:
				return argument.Message;
			case OperationCanceledException:
				return null;
			default:
				Logger.LogWarning("A Docker manager page failed: {Reason}", LogSafe.Describe(exception));
				return exception.Message;
		}
	}

	/// <summary>Several outputs as one, for a change of several commands: the exit is the last one's.</summary>
	private static DockerOutput Combine(List<DockerOutput> outputs) => outputs.Count == 1
		? outputs[0]
		: outputs[^1] with
		{
			Command = string.Join(" && ", outputs.Select(output => output.Command)),
			Output = string.Join('\n', outputs.Select(output => output.Output.TrimEnd()).Where(text => text.Length > 0)),
			Error = string.Join('\n', outputs.Select(output => output.Error.TrimEnd()).Where(text => text.Length > 0)),
		};
}
