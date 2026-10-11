using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.FileBrowser.Browsing;

namespace Mokaterm.UI.FileBrowser;

// Following the terminal's folder. The shell hands the folders over, because this library and the terminal's know
// nothing of each other.
public partial class RemoteFileBrowser
{
	private (string Folder, string? User)? _pendingTerminalFolder;
	private string? _terminalHome;

	/// <summary>Whether the browser follows the folder of the session's terminal. The parent owns the choice.</summary>
	[Parameter]
	public bool FollowTerminal { get; set; }

	/// <summary>Raised when the user flips following. Without a handler the toggle is not offered.</summary>
	[Parameter]
	public EventCallback<bool> FollowTerminalChanged { get; set; }

	private bool CanFollowTerminal => FollowTerminalChanged.HasDelegate;

	private string FollowTitle => FollowTerminal ? "Stop following the terminal's folder" : "Follow the terminal's folder";

	/// <summary>
	/// The terminal's shell moved to <paramref name="folder"/>: an absolute path, or one starting with <c>~</c> for the
	/// home of <paramref name="user"/>. While following, the browser goes there when it is a folder and the browser is
	/// somewhere else. The terminal only reports moves, so a folder the user left by hand stays left until the shell moves
	/// again: following pauses instead of pulling the user back on every prompt.
	/// </summary>
	public Task ShowTerminalFolderAsync(string folder, string? user) => RunSafeAsync(() => FollowAsync(folder, user));

	private Task ToggleFollowTerminalAsync() => FollowTerminalChanged.InvokeAsync(!FollowTerminal);

	/// <summary>Called with every parameter set: a browser hidden while the terminal moved catches up once it shows.</summary>
	private void ApplyFollowParameters()
	{
		if (!FollowTerminal)
		{
			_pendingTerminalFolder = null;
		}
		else if (IsActive && _pendingTerminalFolder is { } pending && RendererInfo.IsInteractive)
		{
			_pendingTerminalFolder = null;
			_ = RunSafeAsync(() => FollowAsync(pending.Folder, pending.User));
		}
	}

	private async Task FollowAsync(string folder, string? user)
	{
		if (_disposed || !FollowTerminal)
		{
			return;
		}

		// A hidden browser would list folders nobody sees, so only the latest move is kept for when it shows.
		if (!IsActive)
		{
			_pendingTerminalFolder = (folder, user);
			return;
		}

		// Busy with a dialog or typing a path: the user is in the middle of something here, and the next move catches up.
		if (!CanBrowse || _editingPath || _operationRunning || _fileSystem is not { } fileSystem || _userFileSystem is not { } login)
		{
			return;
		}

		CancellationToken token = ConnectionToken;
		string target;
		try
		{
			// The login's home, also in root mode: "~" in the login's prompt is theirs.
			_terminalHome ??= RemotePath.Normalize(await login.ResolvePathAsync("~", token));
			if (TerminalFolderTarget.Resolve(folder, user, login.UserName, _terminalHome) is not { } resolved || resolved == _path)
			{
				return;
			}

			if (await fileSystem.StatAsync(resolved, token) is not { IsDirectoryLike: true })
			{
				return;
			}

			target = resolved;
		}
		catch (Exception ex)
		{
			// A folder the account cannot see is not worth an alert on every prompt; the browser just stays.
			Logger.LogDebug("The terminal's folder could not be followed: {Error}", LogSafe.Describe(ex));
			return;
		}

		if (!_disposed && FollowTerminal && ReferenceEquals(fileSystem, _fileSystem))
		{
			await NavigateAsync(target, reportFailure: false);
		}
	}
}
