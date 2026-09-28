using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;

namespace Mokaterm.UI.FileBrowser.Transfers;

/// <summary>
/// The file system a queued transfer uses, found each time the transfer runs rather than once when it was queued. The one
/// it was queued with belongs to a connection that may be gone by then: a session's SFTP connection reopens with a new
/// file system, and a reconnected session brings new ones altogether, so a retry asks the session again.
/// </summary>
internal sealed class TransferFileSystem
{
	private readonly ISessionHandle? _session;
	private readonly Lock _sync = new();
	private IProtocolSession? _rootOwner;
	private Task<IRemoteFileSystem> _root;

	/// <param name="queuedWith">The file system the browser showed when the transfer was queued.</param>
	/// <param name="session">The session it came from. Without one, <paramref name="queuedWith"/> is used for good.</param>
	/// <param name="connection">The live connection <paramref name="queuedWith"/> belongs to.</param>
	public TransferFileSystem(IRemoteFileSystem queuedWith, ISessionHandle? session, IProtocolSession? connection)
	{
		_session = session;
		IsElevated = queuedWith.IsElevated;
		UserName = queuedWith.UserName;
		_rootOwner = connection;
		_root = Task.FromResult(queuedWith);
	}

	/// <summary>True when the transfer runs as root.</summary>
	public bool IsElevated { get; }

	/// <summary>The account the transfer runs as.</summary>
	public string UserName { get; }

	/// <exception cref="RemoteFileSystemException">The session is not connected (<see cref="RemoteFileErrorKind.ConnectionLost"/>).</exception>
	public async ValueTask<IRemoteFileSystem> GetAsync(CancellationToken cancellationToken)
	{
		if (_session is null)
		{
			return await _root;
		}

		IProtocolSession live = _session.Session
			?? throw new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, "The session is not connected.");
		IFileSystemFeature feature = live.GetFeature<IFileSystemFeature>()
			?? throw new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, "The session has no file system any more.");
		if (!IsElevated)
		{
			// The feature hands out the file system it has open, and opens a new one when the old one's connection dropped.
			return await feature.OpenAsync(cancellationToken);
		}

		Task<IRemoteFileSystem> root;
		lock (_sync)
		{
			// A root view outlives a reopened SFTP connection but not the session connection it was opened on. The transfers
			// of one batch share the new one, so sudo asks at most once for them.
			if (!ReferenceEquals(_rootOwner, live) || _root.IsFaulted || _root.IsCanceled)
			{
				_rootOwner = live;
				_root = Task.Run(() => feature.OpenElevatedAsync(CancellationToken.None).AsTask(), CancellationToken.None);
			}

			root = _root;
		}

		return await root.WaitAsync(cancellationToken);
	}
}
