using System.Security.Cryptography;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Modules.Ssh.Connection;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Mokaterm.Modules.Ssh.Keys;

/// <summary>Loads private keys with SSH.NET and reports failures the way the credential editor shows them.</summary>
internal static class SshPrivateKeys
{
	/// <summary>Private keys are a few kilobytes; far larger input is not a key.</summary>
	public const int MaxKeyBytes = 64 * 1024;

	/// <summary>
	/// Parses <paramref name="keyText"/> (UTF-8), decrypting it with <paramref name="passphrase"/> when it is encrypted.
	/// On success <paramref name="keyFile"/> is set and the caller disposes it; otherwise <paramref name="error"/> explains why.
	/// </summary>
	public static PrivateKeyStatus TryLoad(ReadOnlySpan<byte> keyText, string? passphrase, out PrivateKeyFile? keyFile, out string? error)
	{
		keyFile = null;
		ReadOnlySpan<byte> text = keyText.Trim(PrivateKeyEnvelope.Whitespace);
		if (text.Length > MaxKeyBytes)
		{
			error = "This file is too large to be a private key.";
			return PrivateKeyStatus.Invalid;
		}

		PrivateKeyEnvelope envelope = PrivateKeyEnvelope.Detect(text);
		switch (envelope.Format)
		{
			case PrivateKeyFormat.Unknown:
				error = "This is not a private key in OpenSSH, PEM or PuTTY format.";
				return PrivateKeyStatus.Invalid;
			case PrivateKeyFormat.PublicKey:
				error = "This is a public key. Paste the private key instead.";
				return PrivateKeyStatus.Invalid;
			case PrivateKeyFormat.Unsupported:
				error = "This key type is not supported.";
				return PrivateKeyStatus.Unsupported;
		}

		if (envelope.IsEncrypted && string.IsNullOrEmpty(passphrase))
		{
			error = "The key is protected by a passphrase.";
			return PrivateKeyStatus.PassphraseRequired;
		}

		byte[] copy = GC.AllocateArray<byte>(text.Length, pinned: true);
		try
		{
			text.CopyTo(copy);
			using MemoryStream stream = new(copy, writable: false);
			keyFile = new PrivateKeyFile(stream, envelope.IsEncrypted ? passphrase : null);
			error = null;
			return PrivateKeyStatus.Valid;
		}
		catch (SshPassPhraseNullOrEmptyException)
		{
			error = "The key is protected by a passphrase.";
			return PrivateKeyStatus.PassphraseRequired;
		}
		catch (Exception ex) when (ex is NotSupportedException || (ex is SshException && ex.Message.Contains("not supported", StringComparison.OrdinalIgnoreCase)))
		{
			error = "This key type or its encryption is not supported.";
			return PrivateKeyStatus.Unsupported;
		}
		catch (SshException ex) when (ex.Message.StartsWith("Invalid private key file", StringComparison.Ordinal))
		{
			error = "The private key is damaged or incomplete.";
			return PrivateKeyStatus.Invalid;
		}
		catch (Exception) when (envelope.IsEncrypted)
		{
			// Decrypting with the wrong passphrase yields garbage, which fails as a padding, check-bytes or ASN.1 error.
			error = "The passphrase is wrong.";
			return PrivateKeyStatus.WrongPassphrase;
		}
		catch (Exception)
		{
			error = "The private key is damaged or incomplete.";
			return PrivateKeyStatus.Invalid;
		}
		finally
		{
			CryptographicOperations.ZeroMemory(copy);
		}
	}

	/// <summary>The key type, such as <c>ssh-ed25519</c>, <c>ssh-rsa</c> or <c>ecdsa-sha2-nistp256</c>.</summary>
	public static string AlgorithmOf(PrivateKeyFile keyFile) =>
		keyFile.Key.ToString() ?? keyFile.HostKeyAlgorithms.First().Name;

	/// <summary>The OpenSSH <c>SHA256:</c> fingerprint of the public half, as <c>ssh-keygen -l</c> prints it.</summary>
	public static string FingerprintOf(PrivateKeyFile keyFile) =>
		SshHostKeys.Fingerprint(keyFile.HostKeyAlgorithms.First().Data);
}
