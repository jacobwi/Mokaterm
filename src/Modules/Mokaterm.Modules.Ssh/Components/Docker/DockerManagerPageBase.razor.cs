using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.UI.Common.Files;
using Mokaterm.UI.Common.ToolPages;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// The Docker manager's own pages and the panels inside them: a <see cref="DockerPageBase"/>, which loads, confirms
/// changes and polls the way every page does, with the session's Docker client, its files, its update checks and the
/// shell tab beside the public context. The members that touch the module's own types are <c>private protected</c>, so
/// the pages can be public components without making those types public.
/// </summary>
public abstract partial class DockerManagerPageBase : DockerPageBase
{
	[Inject]
	private ISessionManager Sessions { get; set; } = default!;

	[Inject]
	private ISessionTabs Tabs { get; set; } = default!;

	[Inject]
	private IFileBrowserDialog FileBrowser { get; set; } = default!;

	/// <summary>Opens another page of the manager; null outside one, such as in a test.</summary>
	private protected DockerPageContext? Navigation => Context as DockerPageContext;

	private protected DockerSessionFeature? Feature => Session.Session?.GetFeature<DockerSessionFeature>();

	/// <summary>Whether "Open shell" can open a tab for this login: its protocol has to bring a terminal.</summary>
	private protected bool CanOpenShellTab => Session.Protocol.Has(ProtocolCapabilities.Terminal);

	private protected DockerClient? Client => Feature?.Client;

	/// <summary>The docker host's files through the session's own file system, or null for a session that has none.</summary>
	private protected DockerHostFiles? HostFiles => Feature?.HostFiles;

	/// <summary>False when the connection does not run anything as root, so no page offers it.</summary>
	private protected bool CanUseRoot => HostFiles?.CanUseRoot ?? false;

	/// <summary>The session's builds and pushes, which outlive the page that started them.</summary>
	private protected DockerLiveRuns? LiveRuns => Feature?.LiveRuns;

	/// <summary>True while a change of this page or a build or push of the session runs.</summary>
	private protected bool IsWorking => IsBusy || LiveRuns?.IsRunning == true;

	/// <summary>
	/// Asks first, showing every command exactly as it runs (masked values aside), then starts a build or a push in the
	/// background. Null when the user said no, the session went, or another one is still running.
	/// </summary>
	private protected async Task<DockerLiveRun?> StartLiveAsync(DockerLivePlan plan)
	{
		ArgumentNullException.ThrowIfNull(plan);
		if (Feature is not { } feature || IsDisposed)
		{
			return null;
		}

		if (feature.LiveRuns.IsRunning)
		{
			Interaction.Notify(NoticeSeverity.Warning, "A build or a push is still running. Let it finish, or stop it, first.");
			return null;
		}

		DockerClient client = feature.Client;
		ConfirmResult answer = await Interaction.ConfirmAsync(
			new ConfirmPrompt
			{
				Title = plan.Title,
				Message = plan.Confirm,
				ConfirmText = plan.ConfirmText,
				Destructive = false,
				Details = string.Join('\n', DockerSessionFeature.Shown(client, plan.Calls)),
			},
			Lifetime);
		if (!answer.Confirmed || IsDisposed)
		{
			return null;
		}

		try
		{
			return feature.StartLive(plan, client);
		}
		catch (ObjectDisposedException)
		{
			Interaction.Notify(NoticeSeverity.Warning, "The session is closed.");
			return null;
		}
		catch (InvalidOperationException ex)
		{
			Interaction.Notify(NoticeSeverity.Warning, ex.Message);
			return null;
		}
	}

	/// <summary>Runs a load with the session's client; nothing at all while the session has none.</summary>
	private protected Task LoadAsync(Func<DockerClient, CancellationToken, Task> load)
	{
		ArgumentNullException.ThrowIfNull(load);
		return Client is { } client ? LoadAsync(cancellationToken => load(client, cancellationToken)) : Task.CompletedTask;
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
		if (Client is not { } client || calls.Count == 0)
		{
			return null;
		}

		string prefix = client.IsElevated ? "sudo " : "";
		return await RunChangeStepsAsync(
			[.. calls.Select(call => prefix + call.Display)],
			new DockerChangePrompt(title, confirm, confirmText, destructive),
			(index, cancellationToken) => client.RunAsync(calls[index], cancellationToken),

			// Noted for the health watch, which never reports what the user's own change did.
			() => Feature?.Changes.Begin(calls),
			afterwards);
	}

	/// <summary><see cref="RunChangeAsync(DockerCall, string, string?, string, Func{Task}?, bool)"/> for a caller that has no use for the output beyond showing it.</summary>
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
	/// Lets the user pick a folder on the docker host in the built-in file browser, over the session's own files and so as
	/// the login. Null when the session has no files, or the browser was closed without a folder.
	/// </summary>
	/// <param name="startFolder">Where to open, such as what is typed already; null for the login's home.</param>
	private protected async Task<string?> PickHostFolderAsync(string title, string? startFolder)
	{
		ArgumentNullException.ThrowIfNull(title);
		if (Session.Session?.GetFeature<IFileSystemFeature>() is not { } files || Feature is not { } feature)
		{
			Interaction.Notify(NoticeSeverity.Warning, "This session has no files to pick a folder from. Type the folder instead.");
			return null;
		}

		FileBrowserRequest request = new()
		{
			FileSystem = files,
			Title = title,
			Location = feature.Host,
			StartFolder = startFolder is { Length: > 0 } && startFolder.StartsWith('/') ? startFolder : null,
		};
		return await FileBrowser.PickFolderAsync(request, Lifetime);
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
}
