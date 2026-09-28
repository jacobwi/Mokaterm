using System.Text;

namespace Mokaterm.Abstractions.Credentials;

/// <summary>What to generate. Drop the instance as soon as the key is made: strings cannot be wiped.</summary>
public sealed record SshKeyRequest
{
	public required SshKeyType Type { get; init; }

	/// <summary>Trailing label on the <c>authorized_keys</c> line, usually <c>user@machine</c>.</summary>
	public string? Comment { get; init; }

	/// <summary>Encrypts the private key file. Null or empty writes it unprotected.</summary>
	public string? Passphrase { get; init; }

	private bool PrintMembers(StringBuilder builder)
	{
		builder
			.Append("Type = ").Append(Type)
			.Append(", Comment = ").Append(Comment)
			.Append(", Passphrase = ").Append(SecretText.Describe(Passphrase));
		return true;
	}
}
