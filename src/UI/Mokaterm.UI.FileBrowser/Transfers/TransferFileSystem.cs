using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;

namespace Mokaterm.UI.FileBrowser.Transfers;

/// <summary>
/// The file system a queued transfer uses, found each time the transfer runs rather than once when it was queued. The one
/// it was queued with belongs to a connection that may be gone by then: a session's SFTP connection reopens with a new
/// file system, and a reconnected session brings new ones altogether, so a retry asks the session again. A file system a
/// module handed over is asked again the same way, through its feature.
/// </summary>
internal sealed class TransferFileSystem
{
	private readonly Func<(object Owner, IFileSystemFeature Feature)>? _live;
	private readonly Lock _sync = new();
	private object? _rootOwner;
	private Task<IRemoteFileSystem> _root;

	/// <param name="queuedWith">The file system the browser showed when the transfer was queued.</param>
	/// <param name="session">The session it came from. Without one, <paramref name="queuedWith"/> is used for good.</param>
	/// <param name="connection">The live connection <paramref name="queuedWith"/> belongs to.</param>
	public TransferFileSystem(IRemoteFileSystem queuedWith, ISessionHandle? session, IProtocolSession? connection)
		: this(queuedWith, session is null ? null : () => Live(session), connection)
	{
	}

	/// <param name="queuedWith">The file system the browser showed when the transfer was queued.</param>
	/// <param name="feature">The feature it came from, which hands out the file system every time the transfer runs.</param>
	public TransferFileSystem(IRemoteFileSystem queuedWith, IFileSystemFeature feature)
		: this(queuedWith, FixedFeature(feature), feature)
	{
	}

	private TransferFileSystem(IRemoteFileSystem queuedWith, Func<(object Owner, IFileSystemFeature Feature)>? live, object? owner)
	{
		ArgumentNullException.ThrowIfNull(queuedWith);
		_live = live;
		IsElevated = queuedWith.IsElevated;
		UserName = queuedWith.UserName;
		_rootOwner = owner;
		_root = Task.FromResult(queuedWith);
	}

	/// <summary>True when the transfer runs as root.</summary>
	public bool IsElevated { get; }

	/// <summary>The account the transfer runs as.</summary>
	public string UserName { get; }

	/// <exception cref="RemoteFileSystemException">The session is not connected (<see cref="RemoteFileErrorKind.ConnectionLost"/>).</exception>
	public async ValueTask<IRemoteFileSystem> GetAsync(CancellationToken cancellationToken)
	{
		if (_live is null)
		{
			return await _root;
		}

		(object owner, IFileSystemFeature feature) = _live();
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
			if (!ReferenceEquals(_rootOwner, owner) || _root.IsFaulted || _root.IsCanceled)
			{
				_rootOwner = owner;
				_root = Task.Run(() => feature.OpenElevatedAsync(CancellationToken.None).AsTask(), CancellationToken.None);
			}

			root = _root;
		}

		return await root.WaitAsync(cancellationToken);
	}

	private static (object Owner, IFileSystemFeature Feature) Live(ISessionHandle session)
	{
		IProtocolSession live = session.Session
			?? throw new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, "The session is not connected.");
		IFileSystemFeature feature = live.GetFeature<IFileSystemFeature>()
			?? throw new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, "The session has no file system any more.");
		return (live, feature);
	}

	private static Func<(object Owner, IFileSystemFeature Feature)> FixedFeature(IFileSystemFeature feature)
	{
		ArgumentNullException.ThrowIfNull(feature);
		return () => (feature, feature);
	}
}
