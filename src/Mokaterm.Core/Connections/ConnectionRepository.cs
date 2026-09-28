using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Core.Commands;
using Mokaterm.Core.Storage;
using Mokaterm.Core.Validation;

namespace Mokaterm.Core.Connections;

/// <summary>Folders, hosts and connections in the encrypted <c>connections</c> document, cached per UI scope.</summary>
internal sealed class ConnectionRepository : IConnectionRepository, IDisposable
{
	private readonly VaultDocumentCache<ConnectionsDocument> _document;
	private readonly ICredentialStore _credentials;
	private readonly CommandSnippetStore _snippets;
	private readonly TimeProvider _timeProvider;

	public ConnectionRepository(VaultDataStore store, IVault vault, VaultDocumentUpdateLocks updateLocks, ICredentialStore credentials, CommandSnippetStore snippets, TimeProvider timeProvider)
	{
		_document = new VaultDocumentCache<ConnectionsDocument>(ConnectionsDocument.DocumentName, static () => new ConnectionsDocument(), store, vault, updateLocks);
		_document.ExternalChange += OnExternalChange;
		_credentials = credentials;
		_snippets = snippets;
		_timeProvider = timeProvider;
	}

	public event Action? Changed;

	public async ValueTask<ConnectionCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
	{
		ConnectionsDocument document = await _document.GetAsync(cancellationToken);
		return new ConnectionCatalog(document.Folders, document.Hosts, document.Connections);
	}

	public async Task SaveFolderAsync(ConnectionFolder folder, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(folder);
		RequireId(folder.Id, nameof(folder));
		string name = InputText.Require(folder.Name, "A folder needs a name.", nameof(folder));

		await _document.UpdateAsync<bool>(
			document =>
			{
				if (folder.ParentId is { } parentId)
				{
					if (document.Folders.All(existing => existing.Id != parentId))
					{
						throw new ArgumentException("The parent folder does not exist.", nameof(folder));
					}

					if (CreatesCycle(document.Folders, folder.Id, parentId))
					{
						throw new ArgumentException("A folder cannot be moved into itself or one of its subfolders.", nameof(folder));
					}
				}

				ConnectionFolder saved = folder with { Name = name };
				return (document with { Folders = Upsert(document.Folders, saved, static item => item.Id) }, true);
			},
			cancellationToken);

		Changed?.Invoke();
	}

	public async Task DeleteFolderAsync(Guid folderId, CancellationToken cancellationToken = default)
	{
		DateTimeOffset now = _timeProvider.GetUtcNow();
		bool removed = await _document.UpdateAsync<bool>(
			document =>
			{
				if (document.Folders.FirstOrDefault(folder => folder.Id == folderId) is not { } deleted)
				{
					return (null, false);
				}

				return (document with
				{
					Folders = [.. document.Folders
						.Where(folder => folder.Id != folderId)
						.Select(folder => folder.ParentId == folderId ? folder with { ParentId = deleted.ParentId } : folder)],
					Hosts = [.. document.Hosts
						.Select(host => host.FolderId == folderId ? host with { FolderId = deleted.ParentId, UpdatedAt = now } : host)],
				}, true);
			},
			cancellationToken);

		if (removed)
		{
			Changed?.Invoke();
		}
	}

	public async Task SaveHostAsync(HostProfile host, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(host);
		RequireId(host.Id, nameof(host));
		string address = InputText.Require(host.Address, "A host needs an address.", nameof(host));
		if (!Enum.IsDefined(host.Environment))
		{
			throw new ArgumentException("Unknown host environment.", nameof(host));
		}

		DateTimeOffset now = _timeProvider.GetUtcNow();
		string? previousAddress = await _document.UpdateAsync<string?>(
			document =>
			{
				if (host.FolderId is { } folderId && document.Folders.All(folder => folder.Id != folderId))
				{
					throw new ArgumentException("The folder does not exist.", nameof(host));
				}

				HostProfile? existing = document.Hosts.FirstOrDefault(item => item.Id == host.Id);
				HostProfile saved = host with
				{
					Name = host.Name?.Trim() ?? "",
					Address = address,
					Tags = NormalizeTags(host.Tags),
					Color = InputText.TrimToNull(host.Color),
					Notes = InputText.TrimToNull(host.Notes),
					CreatedAt = existing?.CreatedAt ?? now,
					UpdatedAt = now,
				};

				return (document with { Hosts = Upsert(document.Hosts, saved, static item => item.Id) }, existing?.Address);
			},
			cancellationToken);

		if (previousAddress is not null)
		{
			// Saved commands are filed under the address, so they follow the machine when it moves.
			await _snippets.MoveHostAsync(previousAddress, address, cancellationToken);
		}

		Changed?.Invoke();
	}

	public async Task DeleteHostAsync(Guid hostId, CancellationToken cancellationToken = default)
	{
		(string Address, Guid[] ConnectionIds)? removed = await _document.UpdateAsync<(string, Guid[])?>(
			document =>
			{
				if (document.Hosts.FirstOrDefault(host => host.Id == hostId) is not { } deleted)
				{
					return (null, null);
				}

				Guid[] connectionIds = [.. document.Connections.Where(connection => connection.HostId == hostId).Select(connection => connection.Id)];
				return (document with
				{
					Hosts = [.. document.Hosts.Where(host => host.Id != hostId)],
					Connections = [.. document.Connections.Where(connection => connection.HostId != hostId)],
				}, (deleted.Address, connectionIds));
			},
			cancellationToken);

		if (removed is { } gone)
		{
			// Nothing is left to run the commands saved here on.
			await _snippets.ForgetHostAsync(gone.Address, gone.ConnectionIds, cancellationToken);
			await DeleteOwnedCredentialsAndNotifyAsync(gone.ConnectionIds, cancellationToken);
		}
	}

	public async Task SaveConnectionAsync(ConnectionProfile connection, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(connection);
		RequireId(connection.Id, nameof(connection));
		string protocolId = InputText.Require(connection.ProtocolId, "A connection needs a protocol.", nameof(connection)).ToLowerInvariant();
		if (connection.Port is < 1 or > 65535)
		{
			throw new ArgumentOutOfRangeException(nameof(connection), connection.Port, "The port must be between 1 and 65535.");
		}

		if (!Enum.IsDefined(connection.AuthenticationMethod))
		{
			throw new ArgumentException("Unknown authentication method.", nameof(connection));
		}

		if (connection.Terminal?.FontFamily is { } fontFamily && !TerminalFontFamily.IsValid(fontFamily))
		{
			throw new ArgumentException(TerminalFontFamily.Requirement, nameof(connection));
		}

		DateTimeOffset now = _timeProvider.GetUtcNow();
		await _document.UpdateAsync<bool>(
			document =>
			{
				if (document.Hosts.All(host => host.Id != connection.HostId))
				{
					throw new ArgumentException("The host does not exist.", nameof(connection));
				}

				ConnectionProfile? existing = document.Connections.FirstOrDefault(item => item.Id == connection.Id);
				ConnectionProfile saved = connection with
				{
					ProtocolId = protocolId,
					Username = InputText.TrimToNull(connection.Username),
					Label = InputText.TrimToNull(connection.Label),
					Options = connection.Options ?? ProtocolOptions.Empty,
					Terminal = connection.Terminal is { IsEmpty: false } terminal ? terminal : null,
					CreatedAt = existing?.CreatedAt ?? now,
					UpdatedAt = now,

					// Editors hold a copy from before the last connect; MarkConnectedAsync owns this value.
					LastConnectedAt = existing?.LastConnectedAt ?? connection.LastConnectedAt,
				};

				return (document with { Connections = Upsert(document.Connections, saved, static item => item.Id) }, true);
			},
			cancellationToken);

		Changed?.Invoke();
	}

	public async Task DeleteConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default)
	{
		bool removed = await _document.UpdateAsync<bool>(
			document => document.Connections.All(connection => connection.Id != connectionId)
				? (null, false)
				: (document with { Connections = [.. document.Connections.Where(connection => connection.Id != connectionId)] }, true),
			cancellationToken);

		if (removed)
		{
			// The machine is still there, so its commands move to it instead of going with the login.
			await _snippets.ForgetConnectionsAsync([connectionId], cancellationToken);
			await DeleteOwnedCredentialsAndNotifyAsync([connectionId], cancellationToken);
		}
	}

	public async Task MarkConnectedAsync(Guid connectionId, DateTimeOffset connectedAt, CancellationToken cancellationToken = default)
	{
		bool marked = await _document.UpdateAsync<bool>(
			document => document.Connections.All(connection => connection.Id != connectionId)
				? (null, false)
				: (document with
				{
					Connections = [.. document.Connections.Select(connection => connection.Id == connectionId ? connection with { LastConnectedAt = connectedAt } : connection)],
				}, true),
			cancellationToken);

		if (marked)
		{
			Changed?.Invoke();
		}
	}

	public void Dispose()
	{
		_document.ExternalChange -= OnExternalChange;
		_document.Dispose();
	}

	private static void RequireId(Guid id, string paramName)
	{
		if (id == Guid.Empty)
		{
			throw new ArgumentException("An id is required.", paramName);
		}
	}

	/// <summary>True when <paramref name="parentId"/> is <paramref name="folderId"/> or lies below it.</summary>
	private static bool CreatesCycle(IReadOnlyList<ConnectionFolder> folders, Guid folderId, Guid parentId)
	{
		HashSet<Guid> visited = [];
		for (Guid? current = parentId; current is { } id; current = folders.FirstOrDefault(folder => folder.Id == id)?.ParentId)
		{
			// A repeated id means the stored tree already loops; refuse to build on it.
			if (id == folderId || !visited.Add(id))
			{
				return true;
			}
		}

		return false;
	}

	private static string[] NormalizeTags(IReadOnlyList<string>? tags) =>
		tags is null ? [] : [.. tags.Select(InputText.TrimToNull).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)];

	private static List<T> Upsert<T>(IReadOnlyList<T> items, T item, Func<T, Guid> getId)
	{
		Guid id = getId(item);
		List<T> result = new(items.Count + 1);
		bool replaced = false;
		foreach (T existing in items)
		{
			if (getId(existing) == id)
			{
				result.Add(item);
				replaced = true;
			}
			else
			{
				result.Add(existing);
			}
		}

		if (!replaced)
		{
			result.Add(item);
		}

		return result;
	}

	private async Task DeleteOwnedCredentialsAndNotifyAsync(Guid[] connectionIds, CancellationToken cancellationToken)
	{
		try
		{
			if (connectionIds.Length == 0)
			{
				return;
			}

			IReadOnlyList<CredentialInfo> credentials = await _credentials.ListAsync(cancellationToken);
			foreach (CredentialInfo credential in credentials)
			{
				if (!credential.IsShared && credential.OwnerConnectionId is { } ownerId && connectionIds.Contains(ownerId))
				{
					await _credentials.DeleteAsync(credential.Id, cancellationToken);
				}
			}
		}
		finally
		{
			Changed?.Invoke();
		}
	}

	private void OnExternalChange() => Changed?.Invoke();
}
