using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Security;
using Mokaterm.Core.Security;
using Mokaterm.Core.Storage;
using Mokaterm.Core.Validation;

namespace Mokaterm.Core.Commands;

/// <summary>Saved commands in the encrypted <c>commands</c> document, cached per UI scope.</summary>
internal sealed class CommandSnippetStore : ICommandSnippetStore, IDisposable
{
	private readonly VaultDocumentCache<CommandSnippetsDocument> _document;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<CommandSnippetStore> _logger;

	public CommandSnippetStore(VaultDataStore store, IVault vault, VaultDocumentUpdateLocks updateLocks, TimeProvider timeProvider, ILogger<CommandSnippetStore> logger)
	{
		_document = new VaultDocumentCache<CommandSnippetsDocument>(CommandSnippetsDocument.DocumentName, static () => new CommandSnippetsDocument(), store, vault, updateLocks);
		_document.ExternalChange += OnExternalChange;
		_timeProvider = timeProvider;
		_logger = logger;
	}

	public event Action? Changed;

	public async ValueTask<IReadOnlyList<CommandSnippet>> ListAsync(CancellationToken cancellationToken = default)
	{
		CommandSnippetsDocument document = await _document.GetAsync(cancellationToken);
		return [.. document.Snippets.OrderByDescending(snippet => snippet.CreatedAt)];
	}

	public async ValueTask<IReadOnlyList<CommandSnippet>> QueryAsync(CommandSnippetTarget target, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(target);
		string? host = NormalizeHost(target.Host);
		CommandSnippetsDocument document = await _document.GetAsync(cancellationToken);
		return
		[
			.. document.Snippets
				.Where(snippet => Applies(snippet, host, target.ConnectionId))
				.OrderByDescending(snippet => (int)snippet.Scope)
				.ThenByDescending(snippet => snippet.UseCount)
				.ThenByDescending(snippet => snippet.LastUsedAt ?? DateTimeOffset.MinValue)
				.ThenBy(snippet => snippet.Name, StringComparer.CurrentCultureIgnoreCase),
		];
	}

	public async Task<CommandSnippet> SaveAsync(CommandSnippet snippet, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(snippet);
		if (snippet.Id == Guid.Empty)
		{
			throw new ArgumentException("An id is required.", nameof(snippet));
		}

		if (!Enum.IsDefined(snippet.Scope))
		{
			throw new ArgumentException("Unknown command scope.", nameof(snippet));
		}

		string command = InputText.Require(snippet.Command, "A saved command needs some text.", nameof(snippet));
		if (command.AsSpan().ContainsAny('\r', '\n'))
		{
			throw new ArgumentException("A saved command is a single line.", nameof(snippet));
		}

		string? host = NormalizeHost(snippet.Host);
		if (snippet.Scope == CommandSnippetScope.Host && host is null)
		{
			throw new ArgumentException("A command saved for one machine needs its address.", nameof(snippet));
		}

		if (snippet.Scope == CommandSnippetScope.Connection && snippet.ConnectionId.GetValueOrDefault() == Guid.Empty)
		{
			throw new ArgumentException("A command saved for one login needs that login.", nameof(snippet));
		}

		DateTimeOffset now = _timeProvider.GetUtcNow();
		CommandSnippet saved = await _document.UpdateAsync<CommandSnippet>(
			document =>
			{
				CommandSnippet? existing = document.Snippets.FirstOrDefault(item => item.Id == snippet.Id);
				CommandSnippet stored = snippet with
				{
					Name = InputText.TrimToNull(snippet.Name) ?? CommandSnippetName.Derive(command),
					Command = command,
					Tags = NormalizeTags(snippet.Tags),
					Host = snippet.Scope == CommandSnippetScope.Global ? null : host,
					ConnectionId = snippet.Scope == CommandSnippetScope.Connection ? snippet.ConnectionId : null,
					CreatedAt = existing?.CreatedAt ?? now,

					// Editors hold a copy from before the last pick; MarkUsedAsync owns these two.
					LastUsedAt = existing?.LastUsedAt,
					UseCount = existing?.UseCount ?? 0,
				};

				return (document with { Snippets = Upsert(document.Snippets, stored) }, stored);
			},
			cancellationToken);

		Changed?.Invoke();
		return saved;
	}

	public async Task DeleteAsync(Guid snippetId, CancellationToken cancellationToken = default)
	{
		bool removed = await _document.UpdateAsync<bool>(
			document => document.Snippets.All(snippet => snippet.Id != snippetId)
				? (null, false)
				: (document with { Snippets = [.. document.Snippets.Where(snippet => snippet.Id != snippetId)] }, true),
			cancellationToken);

		if (removed)
		{
			Changed?.Invoke();
		}
	}

	/// <summary>
	/// Follows a machine whose address changed, so the commands saved for it keep showing up in its sessions. True when
	/// any command moved.
	/// </summary>
	public async Task<bool> MoveHostAsync(string fromHost, string toHost, CancellationToken cancellationToken = default)
	{
		string? from = NormalizeHost(fromHost);
		string? to = NormalizeHost(toHost);
		if (from is null || to is null || string.Equals(from, to, StringComparison.Ordinal))
		{
			return false;
		}

		bool moved = await _document.UpdateAsync<bool>(
			document =>
			{
				if (!document.Snippets.Any(snippet => Matches(snippet, from)))
				{
					return (null, false);
				}

				return (document with
				{
					Snippets = [.. document.Snippets.Select(snippet => Matches(snippet, from) ? snippet with { Host = toHost.Trim() } : snippet)],
				}, true);
			},
			cancellationToken);

		if (moved)
		{
			Changed?.Invoke();
		}

		return moved;
	}

	/// <summary>
	/// Cleans up after a deleted machine: its own commands go, and so do the ones saved under the logins that went with
	/// it. True when anything was deleted.
	/// </summary>
	public async Task<bool> ForgetHostAsync(string? host, IReadOnlyCollection<Guid> connectionIds, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(connectionIds);
		string? address = NormalizeHost(host);
		if (address is null && connectionIds.Count == 0)
		{
			return false;
		}

		bool Orphaned(CommandSnippet snippet) =>
			(address is not null && Matches(snippet, address))
			|| (snippet.ConnectionId is { } id && connectionIds.Contains(id));

		bool deleted = await _document.UpdateAsync<bool>(
			document => document.Snippets.Any(Orphaned)
				? (document with { Snippets = [.. document.Snippets.Where(snippet => !Orphaned(snippet))] }, true)
				: (null, false),
			cancellationToken);

		if (deleted)
		{
			Changed?.Invoke();
		}

		return deleted;
	}

	/// <summary>
	/// Cleans up after deleted logins. A command saved under one moves to the machine it was saved on, because the work
	/// it belongs to is still there; one with no machine recorded has nothing left to belong to and goes. True when
	/// anything changed.
	/// </summary>
	public async Task<bool> ForgetConnectionsAsync(IReadOnlyCollection<Guid> connectionIds, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(connectionIds);
		if (connectionIds.Count == 0)
		{
			return false;
		}

		bool Orphaned(CommandSnippet snippet) =>
			snippet.Scope == CommandSnippetScope.Connection && snippet.ConnectionId is { } id && connectionIds.Contains(id);

		bool changed = await _document.UpdateAsync<bool>(
			document =>
			{
				if (!document.Snippets.Any(Orphaned))
				{
					return (null, false);
				}

				List<CommandSnippet> kept = [];
				foreach (CommandSnippet snippet in document.Snippets)
				{
					if (!Orphaned(snippet))
					{
						kept.Add(snippet);
					}
					else if (NormalizeHost(snippet.Host) is not null)
					{
						kept.Add(snippet with { Scope = CommandSnippetScope.Host, ConnectionId = null });
					}
				}

				return (document with { Snippets = kept }, true);
			},
			cancellationToken);

		if (changed)
		{
			Changed?.Invoke();
		}

		return changed;
	}

	public async Task MarkUsedAsync(Guid snippetId, CancellationToken cancellationToken = default)
	{
		DateTimeOffset now = _timeProvider.GetUtcNow();
		try
		{
			bool marked = await _document.UpdateAsync<bool>(
				document =>
				{
					if (document.Snippets.All(snippet => snippet.Id != snippetId))
					{
						return (null, false);
					}

					return (document with
					{
						Snippets =
						[
							.. document.Snippets.Select(snippet => snippet.Id == snippetId
								? snippet with { LastUsedAt = now, UseCount = snippet.UseCount + 1 }
								: snippet),
						],
					}, true);
				},
				cancellationToken);

			if (marked)
			{
				Changed?.Invoke();
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// Use counts only sort the picker; failing to record one must not look like the command failed.
			_logger.LogWarning(ex, "Could not record that a saved command was used");
		}
	}

	public void Dispose()
	{
		_document.ExternalChange -= OnExternalChange;
		_document.Dispose();
	}

	private static bool Applies(CommandSnippet snippet, string? host, Guid? connectionId) => snippet.Scope switch
	{
		CommandSnippetScope.Global => true,
		CommandSnippetScope.Host => host is not null && string.Equals(NormalizeHost(snippet.Host), host, StringComparison.Ordinal),
		CommandSnippetScope.Connection => connectionId is { } id && snippet.ConnectionId == id,
		_ => false,
	};

	/// <summary>True when the command was saved on <paramref name="address"/>, whichever scope it has.</summary>
	private static bool Matches(CommandSnippet snippet, string address) =>
		string.Equals(NormalizeHost(snippet.Host), address, StringComparison.Ordinal);

	private static string? NormalizeHost(string? host) =>
		InputText.TrimToNull(host) is { } value ? HostIdentityKey.NormalizeHost(value) : null;

	private static string[] NormalizeTags(IReadOnlyList<string>? tags) =>
		tags is null ? [] : [.. tags.Select(InputText.TrimToNull).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)];

	private static List<CommandSnippet> Upsert(IReadOnlyList<CommandSnippet> snippets, CommandSnippet snippet)
	{
		List<CommandSnippet> result = new(snippets.Count + 1);
		bool replaced = false;
		foreach (CommandSnippet existing in snippets)
		{
			if (existing.Id == snippet.Id)
			{
				result.Add(snippet);
				replaced = true;
			}
			else
			{
				result.Add(existing);
			}
		}

		if (!replaced)
		{
			result.Add(snippet);
		}

		return result;
	}

	private void OnExternalChange() => Changed?.Invoke();
}
