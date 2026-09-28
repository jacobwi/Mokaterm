using Mokaterm.Abstractions.Connections;

namespace Mokaterm.Sessions.Tests.Fakes;

internal sealed class FakeConnectionRepository : IConnectionRepository
{
	private readonly Lock _lock = new();
	private readonly List<(Guid ConnectionId, DateTimeOffset ConnectedAt)> _markedConnected = [];
	private readonly List<ConnectionProfile> _savedConnections = [];
	private ConnectionCatalog _catalog = ConnectionCatalog.Empty;

	public event Action? Changed;

	public IReadOnlyList<(Guid ConnectionId, DateTimeOffset ConnectedAt)> MarkedConnected
	{
		get
		{
			lock (_lock)
			{
				return [.. _markedConnected];
			}
		}
	}

	/// <summary>Every connection passed to <see cref="SaveConnectionAsync"/>, in order.</summary>
	public IReadOnlyList<ConnectionProfile> SavedConnections
	{
		get
		{
			lock (_lock)
			{
				return [.. _savedConnections];
			}
		}
	}

	public ConnectionCatalog Catalog
	{
		get
		{
			lock (_lock)
			{
				return _catalog;
			}
		}
	}

	public void Add(HostProfile host, params ConnectionProfile[] connections)
	{
		lock (_lock)
		{
			_catalog = _catalog with
			{
				Hosts = [.. _catalog.Hosts.Where(existing => existing.Id != host.Id), host],
				Connections = [.. _catalog.Connections.Where(existing => connections.All(added => added.Id != existing.Id)), .. connections],
			};
		}
	}

	/// <summary>Replaces a connection without recording a save, like an edit made in another window.</summary>
	public void Replace(ConnectionProfile connection)
	{
		lock (_lock)
		{
			_catalog = _catalog with
			{
				Connections = [.. _catalog.Connections.Where(existing => existing.Id != connection.Id), connection],
			};
		}
	}

	public ValueTask<ConnectionCatalog> GetCatalogAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Catalog);

	public Task SaveFolderAsync(ConnectionFolder folder, CancellationToken cancellationToken = default)
	{
		lock (_lock)
		{
			_catalog = _catalog with { Folders = [.. _catalog.Folders.Where(existing => existing.Id != folder.Id), folder] };
		}

		Changed?.Invoke();
		return Task.CompletedTask;
	}

	public Task DeleteFolderAsync(Guid folderId, CancellationToken cancellationToken = default)
	{
		lock (_lock)
		{
			_catalog = _catalog with { Folders = [.. _catalog.Folders.Where(existing => existing.Id != folderId)] };
		}

		Changed?.Invoke();
		return Task.CompletedTask;
	}

	public Task SaveHostAsync(HostProfile host, CancellationToken cancellationToken = default)
	{
		Add(host);
		Changed?.Invoke();
		return Task.CompletedTask;
	}

	public Task DeleteHostAsync(Guid hostId, CancellationToken cancellationToken = default)
	{
		lock (_lock)
		{
			_catalog = _catalog with
			{
				Hosts = [.. _catalog.Hosts.Where(existing => existing.Id != hostId)],
				Connections = [.. _catalog.Connections.Where(existing => existing.HostId != hostId)],
			};
		}

		Changed?.Invoke();
		return Task.CompletedTask;
	}

	public Task SaveConnectionAsync(ConnectionProfile connection, CancellationToken cancellationToken = default)
	{
		Replace(connection);
		lock (_lock)
		{
			_savedConnections.Add(connection);
		}

		Changed?.Invoke();
		return Task.CompletedTask;
	}

	public Task DeleteConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default)
	{
		lock (_lock)
		{
			_catalog = _catalog with { Connections = [.. _catalog.Connections.Where(existing => existing.Id != connectionId)] };
		}

		Changed?.Invoke();
		return Task.CompletedTask;
	}

	public Task MarkConnectedAsync(Guid connectionId, DateTimeOffset connectedAt, CancellationToken cancellationToken = default)
	{
		lock (_lock)
		{
			_markedConnected.Add((connectionId, connectedAt));
		}

		return Task.CompletedTask;
	}
}
