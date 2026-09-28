using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Security;
using Mokaterm.Core.Storage;

namespace Mokaterm.Core.Security;

/// <summary>Trusted SSH host keys and TLS certificates in the encrypted <c>known-hosts</c> document.</summary>
internal sealed class KnownHostsStore : IKnownHostsStore, IDisposable
{
	private static readonly TimeSpan LastSeenResolution = TimeSpan.FromDays(1);

	private readonly VaultDocumentCache<KnownHostsDocument> _document;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<KnownHostsStore> _logger;

	public KnownHostsStore(VaultDataStore store, IVault vault, VaultDocumentUpdateLocks updateLocks, TimeProvider timeProvider, ILogger<KnownHostsStore> logger)
	{
		_document = new VaultDocumentCache<KnownHostsDocument>(KnownHostsDocument.DocumentName, static () => new KnownHostsDocument(), store, vault, updateLocks);
		_document.ExternalChange += OnExternalChange;
		_timeProvider = timeProvider;
		_logger = logger;
	}

	public event Action? Changed;

	public async ValueTask<IReadOnlyList<KnownHost>> ListAsync(CancellationToken cancellationToken = default) =>
		(await _document.GetAsync(cancellationToken)).Hosts;

	public async ValueTask<HostIdentityMatch> MatchAsync(HostIdentity identity, CancellationToken cancellationToken = default)
	{
		HostIdentityKey key = HostIdentityKey.From(identity);
		KnownHost? entry = Find(await _document.GetAsync(cancellationToken), key);
		if (entry is null)
		{
			return HostIdentityMatch.Unknown;
		}

		if (!key.SameFingerprint(HostIdentityKey.From(entry)))
		{
			return HostIdentityMatch.Changed;
		}

		DateTimeOffset now = _timeProvider.GetUtcNow();
		if (entry.LastSeenAt is null || now - entry.LastSeenAt.Value >= LastSeenResolution)
		{
			await TouchAsync(key, now, cancellationToken);
		}

		return HostIdentityMatch.Trusted;
	}

	public async Task TrustAsync(HostIdentity identity, CancellationToken cancellationToken = default)
	{
		HostIdentityKey key = HostIdentityKey.From(identity);
		DateTimeOffset now = _timeProvider.GetUtcNow();
		await _document.UpdateAsync<bool>(
			document =>
			{
				List<KnownHost> hosts = [.. document.Hosts.Where(host => !key.SameSlot(HostIdentityKey.From(host)))];
				hosts.Add(key.ToKnownHost(now, now));
				return (document with { Hosts = hosts }, true);
			},
			cancellationToken);

		Changed?.Invoke();
	}

	public async Task RemoveAsync(KnownHost entry, CancellationToken cancellationToken = default)
	{
		HostIdentityKey key = HostIdentityKey.From(entry);
		bool removed = await _document.UpdateAsync<bool>(
			document =>
			{
				List<KnownHost> hosts = [.. document.Hosts.Where(host => !IsSameEntry(key, HostIdentityKey.From(host)))];
				return hosts.Count == document.Hosts.Count ? (null, false) : (document with { Hosts = hosts }, true);
			},
			cancellationToken);

		if (removed)
		{
			Changed?.Invoke();
		}
	}

	public void Dispose()
	{
		_document.ExternalChange -= OnExternalChange;
		_document.Dispose();
	}

	private static KnownHost? Find(KnownHostsDocument document, HostIdentityKey key) =>
		document.Hosts.FirstOrDefault(host => key.SameSlot(HostIdentityKey.From(host)));

	private static bool IsSameEntry(HostIdentityKey key, HostIdentityKey other) => key.SameSlot(other) && key.SameFingerprint(other);

	private async Task TouchAsync(HostIdentityKey key, DateTimeOffset now, CancellationToken cancellationToken)
	{
		try
		{
			bool touched = await _document.UpdateAsync<bool>(
				document =>
				{
					int index = IndexOf(document.Hosts, key);
					if (index < 0)
					{
						return (null, false);
					}

					KnownHost[] hosts = [.. document.Hosts];
					hosts[index] = hosts[index] with { LastSeenAt = now };
					return (document with { Hosts = hosts }, true);
				},
				cancellationToken);

			if (touched)
			{
				Changed?.Invoke();
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// Last-seen dates are informational; failing to record one must not block the connection.
			_logger.LogWarning(ex, "Could not record when a known host was last seen");
		}
	}

	private static int IndexOf(IReadOnlyList<KnownHost> hosts, HostIdentityKey key)
	{
		for (int i = 0; i < hosts.Count; i++)
		{
			if (IsSameEntry(key, HostIdentityKey.From(hosts[i])))
			{
				return i;
			}
		}

		return -1;
	}

	private void OnExternalChange() => Changed?.Invoke();
}
