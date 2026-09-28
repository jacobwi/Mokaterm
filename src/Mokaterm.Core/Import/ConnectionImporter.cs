using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Import;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Core.Import;

/// <summary>
/// Reads a source, tells the user what is already saved, then creates the folders, hosts and logins. Nothing here
/// touches credentials: an import can add a host but never a secret.
/// </summary>
internal sealed class ConnectionImporter : IConnectionImporter
{
	private readonly IReadOnlyList<IConnectionImportSource> _sources;
	private readonly IConnectionRepository _repository;
	private readonly IProtocolRegistry? _protocols;
	private readonly ILogger<ConnectionImporter> _logger;

	public ConnectionImporter(
		IEnumerable<IConnectionImportSource> sources,
		IConnectionRepository repository,
		ILogger<ConnectionImporter> logger,
		IProtocolRegistry? protocols = null)
	{
		ArgumentNullException.ThrowIfNull(sources);
		_sources = [.. sources];
		_repository = repository;
		_protocols = protocols;
		_logger = logger;
	}

	public IReadOnlyList<ImportSourceInfo> Sources => [.. _sources.Select(source => source.Info)];

	public async ValueTask<ImportPreview> PreviewAsync(string sourceId, ImportReadRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (_sources.FirstOrDefault(source => source.Info.Id == sourceId) is not { } source)
		{
			return ImportPreview.Failed(sourceId ?? "", "That import source is not available.");
		}

		ImportPreview preview;
		try
		{
			preview = await source.ReadAsync(request, cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogError(ex, "Reading the {Source} import source failed.", sourceId);
			return ImportPreview.Failed(sourceId, $"{source.Info.DisplayName} could not be read: {ex.Message}");
		}

		if (preview.Entries.Count == 0)
		{
			return preview;
		}

		List<ImportedEntry> entries = [];
		List<ImportSkip> skipped = [.. preview.Skipped];
		foreach (ImportedEntry entry in preview.Entries)
		{
			if (_protocols is not null && _protocols.Find(entry.ProtocolId) is null)
			{
				skipped.Add(new ImportSkip(entry.Name, $"This build has no {entry.ProtocolId} protocol."));
				continue;
			}

			entries.Add(entry);
		}

		ConnectionCatalog catalog;
		try
		{
			catalog = await _repository.GetCatalogAsync(cancellationToken);
		}
		catch (VaultLockedException)
		{
			return preview with { Entries = entries, Skipped = skipped };
		}

		HashSet<string> saved = new(StringComparer.OrdinalIgnoreCase);
		foreach (ConnectionProfile login in catalog.Connections)
		{
			if (catalog.FindHost(login.HostId) is { } host)
			{
				saved.Add(LoginKey(host.Address, login.Username, login.ProtocolId));
			}
		}

		List<ImportedEntry> marked = [.. entries.Select(entry => entry with
		{
			AlreadyExists = saved.Contains(LoginKey(entry.Address, entry.Username, entry.ProtocolId)),
		})];

		return marked.Count == 0
			? ImportPreview.Nothing(preview.SourceId, "Nothing here can be imported into this build.", preview.Location) with { Skipped = skipped }
			: preview with { Entries = marked, Skipped = skipped };
	}

	public async ValueTask<ImportResult> ImportAsync(ImportRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (request.Entries.Count == 0)
		{
			return new ImportResult();
		}

		int folders = 0;
		int hostsCreated = 0;
		int hostsReused = 0;
		int logins = 0;
		List<ImportSkip> skipped = [];
		try
		{
			ConnectionCatalog catalog = await _repository.GetCatalogAsync(cancellationToken);
			Dictionary<string, Guid> folderIds = new(StringComparer.OrdinalIgnoreCase);
			foreach (ConnectionFolder folder in catalog.Folders)
			{
				folderIds.TryAdd(FolderKey(folder.ParentId, folder.Name), folder.Id);
			}

			Guid? root = request.FolderId is { } chosen && catalog.FindFolder(chosen) is not null ? chosen : null;
			if (!string.IsNullOrWhiteSpace(request.NewFolderName))
			{
				string name = request.NewFolderName.Trim();
				bool known = folderIds.ContainsKey(FolderKey(root, name));
				root = await EnsureFolderAsync(name, root, folderIds, cancellationToken);
				if (!known)
				{
					folders++;
				}
			}

			Dictionary<string, Guid> hostIds = new(StringComparer.OrdinalIgnoreCase);
			foreach (HostProfile host in catalog.Hosts)
			{
				hostIds.TryAdd(host.Address, host.Id);
			}

			foreach (ImportedEntry entry in request.Entries)
			{
				cancellationToken.ThrowIfCancellationRequested();
				Guid? folderId = root;
				foreach (string segment in SplitFolderPath(entry.FolderPath))
				{
					string key = FolderKey(folderId, segment);
					if (!folderIds.TryGetValue(key, out Guid existing))
					{
						folderId = await EnsureFolderAsync(segment, folderId, folderIds, cancellationToken);
						folders++;
						continue;
					}

					folderId = existing;
				}

				if (hostIds.TryGetValue(entry.Address, out Guid hostId))
				{
					hostsReused++;
				}
				else
				{
					hostId = Guid.NewGuid();
					await _repository.SaveHostAsync(
						new HostProfile
						{
							Id = hostId,
							Name = entry.Name.Equals(entry.Address, StringComparison.OrdinalIgnoreCase) ? "" : entry.Name,
							Address = entry.Address,
							FolderId = folderId,
							Tags = entry.Tags,
							Environment = entry.Environment,
							Color = entry.Color,
							Notes = Notes(entry),
						},
						cancellationToken);
					hostIds[entry.Address] = hostId;
					hostsCreated++;
				}

				await _repository.SaveConnectionAsync(
					new ConnectionProfile
					{
						Id = Guid.NewGuid(),
						HostId = hostId,
						ProtocolId = entry.ProtocolId,
						Port = entry.Port,
						Username = entry.Username,
						Label = entry.Label,

						// No secret comes across, so a key based login has to be set up again and the rest ask for
						// theirs on the first connect.
						AuthenticationMethod = entry.AuthenticationMethod == AuthenticationMethod.PublicKey
							? AuthenticationMethod.Password
							: entry.AuthenticationMethod,
						Options = entry.Options,
					},
					cancellationToken);
				logins++;
			}
		}
		catch (VaultLockedException)
		{
			return Result("The vault locked before the import finished.");
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogError(ex, "Importing saved hosts failed.");
			return Result($"The import stopped: {ex.Message}");
		}

		return Result(null);

		ImportResult Result(string? error) => new()
		{
			FoldersCreated = folders,
			HostsCreated = hostsCreated,
			HostsReused = hostsReused,
			LoginsCreated = logins,
			Skipped = skipped,
			Error = error,
		};
	}

	private static string LoginKey(string address, string? username, string protocolId) =>
		$"{address}\0{username ?? ""}\0{protocolId}";

	private static string FolderKey(Guid? parentId, string name) => $"{parentId?.ToString("N") ?? ""}\0{name}";

	private static string[] SplitFolderPath(string? path) =>
		string.IsNullOrWhiteSpace(path)
			? []
			: path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

	private static string? Notes(ImportedEntry entry)
	{
		List<string> parts = [];
		if (!string.IsNullOrWhiteSpace(entry.Notes))
		{
			parts.Add(entry.Notes.Trim());
		}

		if (!string.IsNullOrWhiteSpace(entry.IdentityFilePath))
		{
			parts.Add($"Key file: {entry.IdentityFilePath.Trim()} (add it to the keychain to use it).");
		}

		return parts.Count == 0 ? null : string.Join(" ", parts);
	}

	private async Task<Guid> EnsureFolderAsync(string name, Guid? parentId, Dictionary<string, Guid> folderIds, CancellationToken cancellationToken)
	{
		string key = FolderKey(parentId, name);
		if (folderIds.TryGetValue(key, out Guid existing))
		{
			return existing;
		}

		Guid id = Guid.NewGuid();
		await _repository.SaveFolderAsync(new ConnectionFolder { Id = id, Name = name, ParentId = parentId }, cancellationToken);
		folderIds[key] = id;
		return id;
	}
}
