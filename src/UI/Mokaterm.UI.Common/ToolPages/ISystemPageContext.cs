using Mokaterm.Abstractions.ServerAdmin;
using Mokaterm.UI.Common.Editing;

namespace Mokaterm.UI.Common.ToolPages;

/// <summary>
/// What a page of the System view gets from the view around it, cascaded to every page: the server's facts, root mode,
/// scripts through the session's own runner, changes through the view's review, the editor, and the other pages.
/// </summary>
/// <remarks>
/// Every script goes through the same path the built-in pages use: an exec channel from the connection's budget, the
/// view's time limit, <c>LC_ALL=C</c>, the output marker, and sudo when it runs as root. Call members from the
/// renderer's thread.
/// </remarks>
public interface ISystemPageContext
{
	/// <summary>What the server is, as detected when the view opened or last detected again.</summary>
	ServerFacts Server { get; }

	/// <summary>sudo is ready and held for this view.</summary>
	bool RootModeOn { get; }

	/// <summary>A script marked to run as root can run now without asking anything: root mode is on, or the login is root.</summary>
	bool CanReadAsRoot { get; }

	/// <summary>Why changes that need root are not offered on this session, or null when they can run, now or after a sudo prompt.</summary>
	string? RootChangeBlocked { get; }

	/// <summary>A change is under review or running; pages turn their own buttons off meanwhile.</summary>
	bool IsBusy { get; }

	/// <summary>False while the session's tab is hidden: a polling page waits instead of reading what nobody sees.</summary>
	bool IsVisible { get; }

	/// <summary>Turns root mode on, the way the strip's button does; sudo may ask for the password. A refusal shows in the strip.</summary>
	Task EnableRootAsync();

	/// <summary>Opens another page by its id, such as <c>services</c>, with a filter it picks up once through <see cref="TakeHint"/>.</summary>
	void Navigate(string pageId, string? hint = null);

	/// <summary>The hint the current page was opened with, once; later calls get null.</summary>
	string? TakeHint();

	/// <summary>
	/// Runs a read and hands back how it ended. One marked as root needs root mode on, so reading never asks for a
	/// password by surprise; check <see cref="CanReadAsRoot"/> first.
	/// </summary>
	/// <exception cref="SystemScriptException">The script did not run to its end, or needs root mode.</exception>
	/// <exception cref="ArgumentException">The script's name is not one <see cref="SystemScript.IsName"/> takes.</exception>
	Task<SystemScriptResult> ReadAsync(SystemScript script, CancellationToken cancellationToken);

	/// <summary>
	/// Shows the change's exact commands in the view's review, waits for the user, then runs it for the session: it
	/// finishes even when the view closes, and its result stays at the bottom of the view. Root mode is turned on first
	/// when the change needs it. Null when the user cancelled or another change was already under review.
	/// </summary>
	Task<SystemChangeResult?> RunChangeAsync(SystemChange change);

	/// <summary>
	/// Opens <paramref name="path"/> in the built-in editor, as the login or as root (which may ask for the sudo password),
	/// until the user saves or closes it. With <paramref name="createNew"/> the file is created empty first and a taken
	/// name fails. A failure to open comes back in the result rather than as an exception.
	/// </summary>
	Task<RemoteTextEditResult> EditFileAsync(string path, bool asRoot, bool createNew, CancellationToken cancellationToken);
}
