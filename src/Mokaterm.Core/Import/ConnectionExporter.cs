using System.Globalization;
using System.Text.Json;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Import;
using Mokaterm.Core.Serialization;

namespace Mokaterm.Core.Import;

/// <summary>
/// Writes every saved folder, host and login to one file. Nothing secret goes in it, so the file can be copied to
/// another machine and read back through the import wizard, which asks for the passwords again.
/// </summary>
internal sealed class ConnectionExporter : IConnectionExporter
{
	private readonly IConnectionRepository _repository;
	private readonly TimeProvider _timeProvider;

	public ConnectionExporter(IConnectionRepository repository, TimeProvider timeProvider)
	{
		_repository = repository;
		_timeProvider = timeProvider;
	}

	public async ValueTask<ConnectionExport> ExportAsync(CancellationToken cancellationToken = default)
	{
		ConnectionCatalog catalog = await _repository.GetCatalogAsync(cancellationToken);
		Dictionary<Guid, string> folderPaths = BuildFolderPaths(catalog);
		HashSet<string> usedFolders = [];
		HashSet<Guid> hostsWithLogin = [];
		List<ConnectionBundleEntry> entries = [];

		foreach (ConnectionProfile login in catalog.Connections)
		{
			if (catalog.FindHost(login.HostId) is not { } host)
			{
				continue;
			}

			hostsWithLogin.Add(host.Id);
			string? path = host.FolderId is { } folderId ? folderPaths.GetValueOrDefault(folderId) : null;
			if (!string.IsNullOrEmpty(path))
			{
				usedFolders.Add(path);
			}

			entries.Add(new ConnectionBundleEntry
			{
				Name = host.DisplayName,
				Address = host.Address,
				ProtocolId = login.ProtocolId,
				Port = login.Port,
				Username = login.Username,
				AuthenticationMethod = login.AuthenticationMethod,
				Options = login.Options,
				FolderPath = path,
				Notes = host.Notes,
				Tags = host.Tags,
				Environment = host.Environment,
				Color = host.Color,
				Label = login.Label,
			});
		}

		ConnectionBundle bundle = new()
		{
			ExportedAt = _timeProvider.GetUtcNow(),
			Entries = entries,
		};

		return new ConnectionExport
		{
			FileName = string.Create(CultureInfo.InvariantCulture, $"mokaterm-connections-{_timeProvider.GetUtcNow():yyyy-MM-dd}.json"),
			Content = JsonSerializer.SerializeToUtf8Bytes(bundle, MokatermJson.Document),
			Folders = usedFolders.Count,
			Hosts = hostsWithLogin.Count,
			Logins = entries.Count,
			HostsWithoutLogin = catalog.Hosts.Count(host => !hostsWithLogin.Contains(host.Id)),
		};
	}

	/// <summary>Every folder's full path, so an entry can name where its host sits without carrying ids across.</summary>
	private static Dictionary<Guid, string> BuildFolderPaths(ConnectionCatalog catalog)
	{
		Dictionary<Guid, string> paths = [];
		foreach (ConnectionFolder folder in catalog.Folders)
		{
			List<string> segments = [];
			ConnectionFolder? current = folder;

			// A damaged file could name a parent that loops; the folder count is the hard stop.
			for (int depth = 0; current is not null && depth <= catalog.Folders.Count; depth++)
			{
				segments.Insert(0, current.Name);
				current = current.ParentId is { } parentId ? catalog.FindFolder(parentId) : null;
			}

			paths[folder.Id] = string.Join('/', segments);
		}

		return paths;
	}
}
