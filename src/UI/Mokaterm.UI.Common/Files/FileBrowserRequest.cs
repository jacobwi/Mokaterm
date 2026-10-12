using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.Common.Files;

/// <summary>
/// One file system for <see cref="IFileBrowserDialog"/>. A class and not a record, so nothing prints the names it carries.
/// </summary>
public sealed class FileBrowserRequest
{
	/// <summary>
	/// The files to show. The browser opens them through <see cref="IFileSystemFeature.OpenAsync"/>, and every transfer it
	/// queues asks again when it runs, so a retry reaches whatever the feature hands out by then. It belongs to whoever
	/// handed it over and is never disposed by the browser.
	/// </summary>
	public required IFileSystemFeature FileSystem { get; init; }

	/// <summary>The dialog's title, which is also the group the transfer queue lists its transfers under.</summary>
	public required string Title { get; init; }

	/// <summary>
	/// What the files belong to, where a session's browser names its host: the account line and the transfer labels read
	/// <c>user@LOCATION:/path</c>. For a container, its name.
	/// </summary>
	public required string Location { get; init; }

	/// <summary>Where the browser opens: an absolute path, or one starting with <c>~</c>. Null opens the home folder.</summary>
	public string? StartFolder { get; init; }
}
