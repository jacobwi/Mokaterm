namespace Mokaterm.Abstractions.Security;

public enum HostIdentityKind
{
	SshHostKey,
	TlsCertificate,
}

/// <summary>What a server presented to prove who it is: an SSH host key or a TLS certificate.</summary>
public sealed record HostIdentity
{
	public required string Host { get; init; }

	public required int Port { get; init; }

	public required HostIdentityKind Kind { get; init; }

	/// <summary><c>ssh-ed25519</c>, <c>ecdsa-sha2-nistp256</c>, or a certificate key description such as <c>RSA 2048</c>.</summary>
	public required string Algorithm { get; init; }

	/// <summary><c>SHA256:base64</c> for SSH keys (the OpenSSH format), uppercase hex SHA-256 for certificates.</summary>
	public required string Fingerprint { get; init; }

	public string? Subject { get; init; }

	public string? Issuer { get; init; }

	public DateTimeOffset? NotAfter { get; init; }

	/// <summary>For certificates: whether the operating system trusts the chain for this host name.</summary>
	public bool? ChainTrusted { get; init; }
}

/// <summary>A server identity the user decided to trust.</summary>
public sealed record KnownHost
{
	public required string Host { get; init; }

	public required int Port { get; init; }

	public required HostIdentityKind Kind { get; init; }

	public required string Algorithm { get; init; }

	public required string Fingerprint { get; init; }

	public DateTimeOffset AddedAt { get; init; }

	public DateTimeOffset? LastSeenAt { get; init; }
}

public enum HostIdentityMatch
{
	/// <summary>The same identity was trusted before.</summary>
	Trusted,

	/// <summary>Nothing is recorded for this host and port.</summary>
	Unknown,

	/// <summary>A different identity was recorded. Possible man-in-the-middle, or the server was reinstalled.</summary>
	Changed,
}

public enum HostKeyPolicy
{
	/// <summary>Ask about unknown hosts, warn loudly about changed ones.</summary>
	Ask,

	/// <summary>Trust unknown hosts on first use without asking; still refuse changed identities without asking.</summary>
	AcceptNew,

	/// <summary>Refuse anything not already trusted.</summary>
	Strict,
}
