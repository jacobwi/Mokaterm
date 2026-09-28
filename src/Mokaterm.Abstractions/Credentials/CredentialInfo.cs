namespace Mokaterm.Abstractions.Credentials;

/// <summary>Credential metadata. Safe to render; the secret itself only comes out of <see cref="ICredentialStore.RevealAsync"/>.</summary>
public sealed record CredentialInfo
{
	public required Guid Id { get; init; }

	public required string Name { get; init; }

	public required CredentialKind Kind { get; init; }

	/// <summary>Suggested username when the credential is picked for a connection.</summary>
	public string? Username { get; init; }

	/// <summary>
	/// Shared credentials show in the keychain and can back any number of connections. Private ones belong
	/// to <see cref="OwnerConnectionId"/> and are deleted with it.
	/// </summary>
	public bool IsShared { get; init; }

	public Guid? OwnerConnectionId { get; init; }

	/// <summary>Key type such as <c>ssh-ed25519</c>, filled in when a private key is saved.</summary>
	public string? KeyAlgorithm { get; init; }

	/// <summary>Public key fingerprint in <c>SHA256:...</c> form.</summary>
	public string? KeyFingerprint { get; init; }

	public bool HasPassphrase { get; init; }

	/// <summary>True when a proxy password is stored with this credential, so an editor can offer to keep it.</summary>
	public bool HasProxyPassword { get; init; }

	public string? Notes { get; init; }

	public DateTimeOffset CreatedAt { get; init; }

	public DateTimeOffset UpdatedAt { get; init; }
}
