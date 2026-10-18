using Mokaterm.Abstractions.ServerAdmin;
using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Detection;
using Mokaterm.Modules.Ssh.Admin.Monitoring;
using Mokaterm.UI.Common.Editing;
using Mokaterm.UI.Common.ToolPages;

namespace Mokaterm.Modules.Ssh.Components.Admin;

/// <summary>
/// What every page of the system view gets from the view around it. The view's own pages use it with the module's
/// types; a page from another module gets the same object through <see cref="ISystemPageContext"/>, whose scripts and
/// changes go through the same feature, review and limits.
/// </summary>
internal sealed class AdminPageContext : ISystemPageContext
{
	private string? _hint;

	public required SystemAdminFeature Feature { get; init; }

	public required SystemProfile Profile { get; init; }

	/// <summary>Shows the action's commands, waits for the user, then runs it. Null when the user cancelled.</summary>
	public required Func<AdminAction, AdminInput?, Task<AdminActionResult?>> Run { get; init; }

	/// <summary>Turns root mode on from inside a page, the way the header's button does.</summary>
	public required Func<Task> EnableRoot { get; init; }

	/// <summary>Shows another page by its id; <see cref="Navigate"/> is what pages call.</summary>
	public required Action<string> Show { get; init; }

	/// <summary>The address this session connected to, as the connection names it.</summary>
	public required string SessionAddress { get; init; }

	/// <summary>True while a change is under review or running; pages turn their buttons off meanwhile.</summary>
	public required Func<bool> IsBusy { get; init; }

	/// <summary>False while the session's tab is hidden: a polling page waits instead of reading what nobody sees.</summary>
	public required Func<bool> IsVisible { get; init; }

	/// <summary>Reviews and runs a reboot, then reconnects the session once the server answers again.</summary>
	public required Func<Task> Reboot { get; init; }

	/// <summary>How this session logged in, for the changes that could refuse its next login. Read once and kept.</summary>
	public required Func<Task<SessionLogin>> Login { get; init; }

	/// <summary>The editor of the window the view is in, for pages that open a file.</summary>
	public required IRemoteTextEditor Editor { get; init; }

	/// <summary>
	/// The session's live figures, which the status bar may be reading as well: one sampler per session, so the overview
	/// and the status bar never run two samples at once. Its lines last as long as the session, and the next sample's
	/// rates cover any time away.
	/// </summary>
	public required HostMonitor Monitor { get; init; }

	/// <summary>The account this session logged in as.</summary>
	public string LoginName => Profile.Facts.UserName;

	/// <summary>Changes that need root can run here, now or after a sudo prompt.</summary>
	public bool CanChangeAsRoot => Feature.CanActAsRoot || Feature.RootBlockedReason is null;

	/// <summary>Why changes that need root are not offered, or null.</summary>
	public string? RootChangeBlocked => CanChangeAsRoot ? null : Feature.RootBlockedReason;

	public ServerFacts Server => Profile.ServerFacts;

	public bool RootModeOn => Feature.RootModeOn;

	public bool CanReadAsRoot => Feature.CanActAsRoot;

	bool ISystemPageContext.IsBusy => IsBusy();

	bool ISystemPageContext.IsVisible => IsVisible();

	Task ISystemPageContext.EnableRootAsync() => EnableRoot();

	/// <summary>Opens the page with <paramref name="pageId"/>, with a filter already chosen when there is a hint, such as the failed services.</summary>
	public void Navigate(string pageId, string? hint = null)
	{
		ArgumentNullException.ThrowIfNull(pageId);
		_hint = hint;
		Show(pageId);
	}

	/// <summary>The filter the page was opened with, once; later calls get null.</summary>
	public string? TakeHint()
	{
		string? hint = _hint;
		_hint = null;
		return hint;
	}

	public async Task<SystemScriptResult> ReadAsync(SystemScript script, CancellationToken cancellationToken) =>
		SystemPageScripts.ToResult(await Feature.ReadAsync(SystemPageScripts.ToAdminScript(script, change: false, readsInput: false), cancellationToken));

	public async Task<SystemChangeResult?> RunChangeAsync(SystemChange change)
	{
		AdminAction action = SystemPageScripts.ToAction(change);
		using AdminInput? input = SystemPageScripts.ToInput(change);
		AdminActionResult? result = await Run(action, input);
		return result is null ? null : SystemPageScripts.ToResult(result);
	}

	public Task<RemoteTextEditResult> EditFileAsync(string path, bool asRoot, bool createNew, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		return AdminFileEditing.EditAsync(Editor, Feature, path, asRoot, createNew, cancellationToken);
	}
}
