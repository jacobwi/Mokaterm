using System.Security.Cryptography;
using System.Text;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Core.Security;

/// <summary>
/// A server identity in normalized form: host trimmed, lowercased and without IPv6 brackets, certificate fingerprints
/// as uppercase hex without separators. Entries match on host, port, kind and algorithm.
/// </summary>
internal readonly record struct HostIdentityKey(string Host, int Port, HostIdentityKind Kind, string Algorithm, string Fingerprint)
{
	public static HostIdentityKey From(HostIdentity identity)
	{
		ArgumentNullException.ThrowIfNull(identity);
		return Create(identity.Host, identity.Port, identity.Kind, identity.Algorithm, identity.Fingerprint);
	}

	public static HostIdentityKey From(KnownHost entry)
	{
		ArgumentNullException.ThrowIfNull(entry);
		return Create(entry.Host, entry.Port, entry.Kind, entry.Algorithm, entry.Fingerprint);
	}

	public static string NormalizeHost(string host)
	{
		string trimmed = host.Trim();
		if (trimmed.Length > 2 && trimmed[0] == '[' && trimmed[^1] == ']')
		{
			trimmed = trimmed[1..^1];
		}

		return trimmed.ToLowerInvariant();
	}

	/// <summary>Same host, port, kind and algorithm; the fingerprint may differ.</summary>
	public bool SameSlot(HostIdentityKey other) =>
		Port == other.Port
		&& Kind == other.Kind
		&& string.Equals(Host, other.Host, StringComparison.Ordinal)
		&& string.Equals(Algorithm, other.Algorithm, StringComparison.Ordinal);

	/// <summary>Compares fingerprints in constant time.</summary>
	public bool SameFingerprint(HostIdentityKey other) =>
		CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Fingerprint), Encoding.UTF8.GetBytes(other.Fingerprint));

	public KnownHost ToKnownHost(DateTimeOffset addedAt, DateTimeOffset? lastSeenAt) => new()
	{
		Host = Host,
		Port = Port,
		Kind = Kind,
		Algorithm = Algorithm,
		Fingerprint = Fingerprint,
		AddedAt = addedAt,
		LastSeenAt = lastSeenAt,
	};

	private static HostIdentityKey Create(string host, int port, HostIdentityKind kind, string algorithm, string fingerprint)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(host);
		ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);
		ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);

		string normalizedFingerprint = fingerprint.Trim();
		if (kind == HostIdentityKind.TlsCertificate)
		{
			// Certificate fingerprints are hex; tools disagree on case and colons, which must not look like a changed certificate.
			normalizedFingerprint = normalizedFingerprint.Replace(":", "", StringComparison.Ordinal).ToUpperInvariant();
		}

		return new HostIdentityKey(NormalizeHost(host), port, kind, algorithm.Trim(), normalizedFingerprint);
	}
}
