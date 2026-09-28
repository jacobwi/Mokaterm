using System.Text;

namespace Mokaterm.Abstractions.Credentials;

/// <summary>
/// A fresh key pair. <see cref="PrivateKey"/> is secret: hand it to <see cref="ICredentialStore.SaveAsync"/> and drop
/// the reference. It only reaches a plain file when the user explicitly exports it.
/// </summary>
public sealed record GeneratedSshKey
{
	/// <summary>OpenSSH private key text, encrypted when the request carried a passphrase.</summary>
	public required string PrivateKey { get; init; }

	public required SshPublicKey PublicKey { get; init; }

	public bool HasPassphrase { get; init; }

	/// <summary>File name OpenSSH would use, such as <c>id_ed25519</c>.</summary>
	public required string SuggestedFileName { get; init; }

	private bool PrintMembers(StringBuilder builder)
	{
		builder
			.Append("PrivateKey = ").Append(SecretText.Describe(PrivateKey))
			.Append(", PublicKey = ").Append(PublicKey)
			.Append(", HasPassphrase = ").Append(HasPassphrase)
			.Append(", SuggestedFileName = ").Append(SuggestedFileName);
		return true;
	}
}
