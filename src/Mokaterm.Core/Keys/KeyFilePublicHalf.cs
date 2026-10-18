using Mokaterm.Abstractions.Credentials;
using Mokaterm.Core.Security;

namespace Mokaterm.Core.Keys;

/// <summary>
/// The public half of a private key file, read without its passphrase: OpenSSH keeps it in the clear in its container
/// and PuTTY in its public lines. The keychain's "Copy public key", the system view's authorized keys and a key saved
/// still locked by its passphrase all read it here.
/// </summary>
internal static class KeyFilePublicHalf
{
	public static bool TryRead(string text, out byte[] publicBlob, out string comment)
	{
		if (OpenSshPrivateKeyFile.TryReadPublicPart(text, out publicBlob, out comment))
		{
			return true;
		}

		publicBlob = [];
		comment = "";
		if (string.IsNullOrEmpty(text) || text.Length > PuttyKeyFile.MaxTextLength)
		{
			return false;
		}

		// An unencrypted .ppk carries its private half in the same text, so the copy goes to pinned memory that is wiped.
		using PinnedBytes bytes = PinnedBytes.FromString(text);
		if (!PuttyKeyFile.LooksLikeOne(bytes.Span)
			|| PuttyKeyFile.TryReadPublicPart(bytes.Span, out PuttyPublicPart? part, out _) != PrivateKeyStatus.Valid
			|| part is null)
		{
			return false;
		}

		publicBlob = part.PublicBlob;
		comment = part.Comment;
		return true;
	}

	/// <summary>The key type and <c>SHA256:</c> fingerprint the keychain records, or nulls when the text holds no readable public half.</summary>
	public static (string? Algorithm, string? Fingerprint) Describe(string text)
	{
		if (!TryRead(text, out byte[] blob, out string comment))
		{
			return (null, null);
		}

		SshPublicKey key = SshKeyGenerator.Describe(blob, comment);
		return (key.Algorithm, key.Fingerprint);
	}
}
