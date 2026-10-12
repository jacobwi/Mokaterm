using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Common.Files;
using Mokaterm.UI.FileBrowser.Transfers;

namespace Mokaterm.UI.FileBrowser.Browsing;

/// <summary>
/// What a <see cref="RemoteFileBrowser"/> shows: a session tab's files, followed across reconnects, or a file system a
/// module handed over through <see cref="IFileBrowserDialog"/>, which is there for as long as the browser is. Both go
/// through the same opening, listing and transfers; these answers are all that differs, and a module's files simply have
/// no terminal, no saved login to keep bookmarks under and no session state.
/// </summary>
internal sealed class BrowserSource
{
	private readonly FileBrowserRequest? _request;

	private BrowserSource(ISessionHandle? session, FileBrowserRequest? request)
	{
		Session = session;
		_request = request;
	}

	/// <summary>The session tab, or null for a module's file system.</summary>
	public ISessionHandle? Session { get; }

	/// <summary>The parameter this source was made from, so the browser notices a different one.</summary>
	public object Key => (object?)Session ?? _request!;

	/// <summary>
	/// The live connection the files come from, or null while a session is not connected. A reconnect hands out a new
	/// one, which is how the browser knows to open the files again; a module's file system is its own connection.
	/// </summary>
	public object? Connection => Session is { } session
		? session.State == SessionState.Connected ? session.Session : null
		: _request!.FileSystem;

	/// <summary>Where the files are, for the account line and the transfer labels: the session's host, or what the module named.</summary>
	public string Location => Session?.Host.Address ?? _request!.Location;

	/// <summary>The group the transfer queue lists this browser's transfers under.</summary>
	public string Group => Session?.Title ?? _request!.Title;

	/// <summary>The session's terminal, for "Open terminal here"; null when there is none to type into.</summary>
	public ITerminalStream? Terminal => Session?.Terminal;

	/// <summary>The saved login bookmarks are kept under, or null: a quick connection and a module's files have none.</summary>
	public Guid? BookmarkOwner => Session is { IsTransient: false } session ? session.Connection.Id : null;

	/// <summary>The start folder a session's connection names in its options, or null.</summary>
	public string? ConfiguredStartFolder(IReadOnlyList<string> optionKeys)
	{
		ArgumentNullException.ThrowIfNull(optionKeys);
		if (Session is not { } session)
		{
			return null;
		}

		foreach (string key in optionKeys)
		{
			if (session.Connection.Options.GetString(key) is { } value && !string.IsNullOrWhiteSpace(value))
			{
				return value.Trim();
			}
		}

		return null;
	}

	public static BrowserSource For(ISessionHandle session)
	{
		ArgumentNullException.ThrowIfNull(session);
		return new BrowserSource(session, null);
	}

	public static BrowserSource For(FileBrowserRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		return new BrowserSource(null, request);
	}

	/// <summary>The file system feature of <paramref name="connection"/>, one <see cref="Connection"/> returned.</summary>
	public IFileSystemFeature? FeatureOf(object connection) => Session is null
		? _request!.FileSystem
		: (connection as IProtocolSession)?.GetFeature<IFileSystemFeature>();

	/// <summary>
	/// What a transfer queued from <paramref name="fileSystem"/> asks for its file system each time it runs: the session,
	/// whatever connection it has by then, or the module's feature.
	/// </summary>
	/// <param name="connection">The connection <paramref name="fileSystem"/> came from.</param>
	public TransferFileSystem ForTransfers(IRemoteFileSystem fileSystem, object? connection) => Session is { } session
		? new TransferFileSystem(fileSystem, session, connection as IProtocolSession)
		: new TransferFileSystem(fileSystem, _request!.FileSystem);

	/// <summary>Listens for the session's changes; a module's file system never changes, so nothing is listened to.</summary>
	public void Subscribe(Action changed)
	{
		if (Session is { } session)
		{
			session.Changed += changed;
		}
	}

	public void Unsubscribe(Action changed)
	{
		if (Session is { } session)
		{
			session.Changed -= changed;
		}
	}
}
