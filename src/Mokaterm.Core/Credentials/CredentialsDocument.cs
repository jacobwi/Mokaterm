using Mokaterm.Abstractions.Credentials;

namespace Mokaterm.Core.Credentials;

/// <summary>The encrypted <c>credentials</c> vault document.</summary>
internal sealed record CredentialsDocument
{
	public const string DocumentName = "credentials";

	public int Version { get; init; } = 1;

	public IReadOnlyList<StoredCredential> Credentials { get; init; } = [];
}

/// <summary>
/// Metadata plus each secret sealed on its own (<c>nonce | tag | ciphertext</c>), so the cached document never holds
/// a decrypted secret.
/// </summary>
internal sealed record StoredCredential
{
	public required CredentialInfo Info { get; init; }

	public byte[]? Password { get; init; }

	public byte[]? PrivateKey { get; init; }

	public byte[]? Passphrase { get; init; }

	public byte[]? ProxyPassword { get; init; }
}
