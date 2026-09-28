namespace Mokaterm.Core.Storage;

/// <summary>
/// Serializes read-modify-write cycles on one encrypted document across every UI scope, so two windows editing
/// connections at once cannot lose each other's changes.
/// </summary>
internal sealed class VaultDocumentUpdateLocks
{
	private readonly AsyncKeyedLock _locks = new();

	public ValueTask<AsyncKeyedLock.Releaser> AcquireAsync(string documentName, CancellationToken cancellationToken) =>
		_locks.AcquireAsync(documentName, cancellationToken);
}
