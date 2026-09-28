namespace Mokaterm.Abstractions.Connections;

/// <summary>
/// Folders, hosts and connections, encrypted at rest with the vault key. Every member throws
/// <see cref="Security.VaultLockedException"/> while the vault is locked. Save methods insert or update
/// and stamp the timestamps.
/// </summary>
public interface IConnectionRepository
{
	/// <summary>Raised after any change, including changes made from another window or browser tab.</summary>
	event Action? Changed;

	ValueTask<ConnectionCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);

	Task SaveFolderAsync(ConnectionFolder folder, CancellationToken cancellationToken = default);

	/// <summary>Deletes a folder. Its child folders and hosts move up to the deleted folder's parent.</summary>
	Task DeleteFolderAsync(Guid folderId, CancellationToken cancellationToken = default);

	Task SaveHostAsync(HostProfile host, CancellationToken cancellationToken = default);

	/// <summary>Deletes a host, its connections and the private credentials those connections own.</summary>
	Task DeleteHostAsync(Guid hostId, CancellationToken cancellationToken = default);

	Task SaveConnectionAsync(ConnectionProfile connection, CancellationToken cancellationToken = default);

	/// <summary>Deletes a connection and the private credential it owns. Shared credentials stay.</summary>
	Task DeleteConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default);

	Task MarkConnectedAsync(Guid connectionId, DateTimeOffset connectedAt, CancellationToken cancellationToken = default);
}
