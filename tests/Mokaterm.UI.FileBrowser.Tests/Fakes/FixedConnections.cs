using Mokaterm.Abstractions.Connections;

namespace Mokaterm.UI.FileBrowser.Tests.Fakes;

/// <summary>A catalog of logins and nothing else; every change is refused, because nothing under test should make one.</summary>
internal sealed class FixedConnections : IConnectionRepository
{
	private static readonly Guid HostId = Guid.NewGuid();

	public FixedConnections(params Guid[] connectionIds) =>
		Catalog = new ConnectionCatalog(
			[],
			[],
			[.. connectionIds.Select(id => new ConnectionProfile { Id = id, HostId = HostId, ProtocolId = "ssh" })]);

	public event Action? Changed
	{
		add { }
		remove { }
	}

	public ConnectionCatalog Catalog { get; set; }

	public ValueTask<ConnectionCatalog> GetCatalogAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Catalog);

	public Task SaveFolderAsync(ConnectionFolder folder, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	public Task DeleteFolderAsync(Guid folderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	public Task SaveHostAsync(HostProfile host, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	public Task DeleteHostAsync(Guid hostId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	public Task SaveConnectionAsync(ConnectionProfile connection, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	public Task DeleteConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	public Task MarkConnectedAsync(Guid connectionId, DateTimeOffset connectedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
