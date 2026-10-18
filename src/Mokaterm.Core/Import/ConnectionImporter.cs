using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Import;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Core.Keys;

namespace Mokaterm.Core.Import;

/// <summary>
/// Reads a source, tells the user what is already saved, then creates the folders, hosts and logins. A password never
/// crosses. On the desktop, the key file a login names can be copied into the keychain (<see cref="KeychainImport"/>);
/// everywhere else the path only goes into the host's notes.
/// </summary>
internal sealed class ConnectionImporter : IConnectionImporter
{
	private readonly IReadOnlyList<IConnectionImportSource> _sources;
	private readonly IConnectionRepository _repository;
	private readonly IProtocolRegistry? _protocols;
	private readonly ICredentialStore? _credentials;
	private readonly IdentityFileReader? _keyFiles;
	private readonly ILogger<ConnectionImporter> _logger;

	public ConnectionImporter(
		IEnumerable<IConnectionImportSource> sources,
		IConnectionRepository repository,
		ILogger<ConnectionImporter> logger,
		IProtocolRegistry? protocols = null,
		ICredentialStore? credentials = null,
		IdentityFileReader? keyFiles = null)
	{
		ArgumentNullException.ThrowIfNull(sources);
		_sources = [.. sources];
		_repository = repository;
		_protocols = protocols;
		_credentials = credentials;
		_keyFiles = keyFiles;
		_logger = logger;
	}

	public IReadOnlyList<ImportSourceInfo> Sources => [.. _sources.Select(source => source.Info)];

	public bool CanImportKeys => _credentials is not null && _keyFiles is { IsAvailable: true };

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

		if (marked.Count > 0 && CanImportKeys && marked.Any(entry => !string.IsNullOrWhiteSpace(entry.IdentityFilePath)))
		{
			marked = await CheckKeysAsync(marked, fromPickedFile: request.File is not null, cancellationToken);
		}

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
		int loginsWithKeys = 0;
		List<ImportSkip> skipped = [];
		List<ImportSkip> keysNotAdded = [];
		KeychainImport? keys = null;
		try
		{
			ConnectionCatalog catalog = await _repository.GetCatalogAsync(cancellationToken);
			if (request.ImportKeys && CanImportKeys)
			{
				keys = await KeychainImport.StartAsync(_credentials!, _keyFiles!, cancellationToken);
			}

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

				Guid? credentialId = null;
				if (keys is not null && entry.KeyFile is { IsUsable: true, Path: { } keyPath })
				{
					(credentialId, string? problem) = await keys.CredentialForAsync(keyPath, cancellationToken);
					if (problem is not null)
					{
						keysNotAdded.Add(new ImportSkip(entry.Name, problem));
					}
				}
				else if (keys is not null && entry.KeyFile is { Reason: { } reason })
				{
					keysNotAdded.Add(new ImportSkip(entry.Name, reason));
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
							Notes = Notes(entry, keyInKeychain: credentialId is not null),
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

						// Without its key in the keychain a key based login has to be set up again, and every other one asks
						// for its secret on the first connect.
						AuthenticationMethod = credentialId is not null
							? AuthenticationMethod.PublicKey
							: entry.AuthenticationMethod == AuthenticationMethod.PublicKey
								? AuthenticationMethod.Password
								: entry.AuthenticationMethod,
						CredentialId = credentialId,
						Options = entry.Options,
					},
					cancellationToken);
				logins++;
				if (credentialId is not null)
				{
					loginsWithKeys++;
				}
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
			KeysAdded = keys?.Added ?? 0,
			LoginsWithKeys = loginsWithKeys,
			Skipped = skipped,
			KeysNotAdded = keysNotAdded,
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

	private static string? Notes(ImportedEntry entry, bool keyInKeychain)
	{
		List<string> parts = [];
		if (!string.IsNullOrWhiteSpace(entry.Notes))
		{
			parts.Add(entry.Notes.Trim());
		}

		if (!keyInKeychain && !string.IsNullOrWhiteSpace(entry.IdentityFilePath))
		{
			parts.Add($"Key file: {entry.IdentityFilePath.Trim()} (add it to the keychain to use it).");
		}

		return parts.Count == 0 ? null : string.Join(" ", parts);
	}

	/// <summary>
	/// Works out what each named key file would become, one read per file however many logins name it. Off the caller's
	/// thread, because a config can name many files and this may be the renderer's.
	/// </summary>
	private async Task<List<ImportedEntry>> CheckKeysAsync(List<ImportedEntry> entries, bool fromPickedFile, CancellationToken cancellationToken)
	{
		IReadOnlyList<CredentialInfo> keychain;
		try
		{
			keychain = await _credentials!.ListAsync(cancellationToken);
		}
		catch (VaultLockedException)
		{
			keychain = [];
		}

		Dictionary<string, CredentialInfo> byFingerprint = KeychainImport.SharedKeys(keychain);
		IdentityFileReader reader = _keyFiles!;
		return await Task.Run(
			() =>
			{
				Dictionary<string, ImportedKeyFile> byPath = new(KeychainImport.PathComparer);
				List<ImportedEntry> checkedEntries = new(entries.Count);
				foreach (ImportedEntry entry in entries)
				{
					cancellationToken.ThrowIfCancellationRequested();
					checkedEntries.Add(string.IsNullOrWhiteSpace(entry.IdentityFilePath)
						? entry
						: entry with { KeyFile = CheckKey(reader, entry, fromPickedFile, byPath, byFingerprint) });
				}

				return checkedEntries;
			},
			cancellationToken);
	}

	private static ImportedKeyFile CheckKey(
		IdentityFileReader reader,
		ImportedEntry entry,
		bool fromPickedFile,
		Dictionary<string, ImportedKeyFile> byPath,
		Dictionary<string, CredentialInfo> keychain)
	{
		IdentityPathResult location = reader.Locate(entry);
		if (location.FullPath is not { } path)
		{
			return new ImportedKeyFile { Status = ImportedKeyStatus.Missing, Reason = location.Reason };
		}

		if (byPath.TryGetValue(path, out ImportedKeyFile? known))
		{
			return known;
		}

		ImportedKeyFile state;
		using (KeyFileMaterial? material = reader.Read(path))
		{
			state = material switch
			{
				null => new ImportedKeyFile
				{
					Status = ImportedKeyStatus.Missing,
					Path = path,
					Reason = MissingReason(path, location.WasRelative, fromPickedFile),
				},
				{ Status: ImportedKeyStatus.Ready } usable when usable.Fingerprint is { } fingerprint && keychain.TryGetValue(fingerprint, out CredentialInfo? existing) =>
					new ImportedKeyFile
					{
						Status = ImportedKeyStatus.InKeychain,
						Path = path,
						Algorithm = usable.Algorithm,
						Fingerprint = fingerprint,
						KeychainName = existing.Name,
					},
				{ Status: ImportedKeyStatus.Ready } usable => new ImportedKeyFile
				{
					Status = ImportedKeyStatus.Ready,
					Path = path,
					Algorithm = usable.Algorithm,
					Fingerprint = usable.Fingerprint,
					NeedsPassphrase = usable.NeedsPassphrase,
				},
				{ } failed => new ImportedKeyFile { Status = failed.Status, Path = path, Reason = failed.Reason },
			};
		}

		byPath[path] = state;
		return state;
	}

	private static string MissingReason(string path, bool relative, bool fromPickedFile) =>
		relative && fromPickedFile
			? $"There is no key file at {path}. A config picked from a file has no folder of its own, so a relative key path is looked for in ~/.ssh."
			: $"There is no key file at {path}.";

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
