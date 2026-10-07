using Mokaterm.Abstractions.Connections;

namespace Mokaterm.UI.Tests.Fakes;

/// <summary>
/// A repository over one fixed catalog, for editor tests: reads answer with it and a save is accepted, so the catalog
/// cannot shift under the render a test is looking at.
/// </summary>
internal sealed class EditableCatalog(ConnectionCatalog catalog) : IConnectionRepository
{
	public event Action? Changed
	{
		add { }
		remove { }
	}

	public ValueTask<ConnectionCatalog> GetCatalogAsync(CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(catalog);

	public Task SaveFolderAsync(ConnectionFolder folder, CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task DeleteFolderAsync(Guid folderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	public Task SaveHostAsync(HostProfile host, CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task DeleteHostAsync(Guid hostId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	public Task SaveConnectionAsync(ConnectionProfile connection, CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task DeleteConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	public Task MarkConnectedAsync(Guid connectionId, DateTimeOffset connectedAt, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();
}
