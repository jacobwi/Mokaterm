namespace Mokaterm.Abstractions.Credentials;

/// <summary>
/// Reads OpenSSH security key files (<c>ssh-keygen -t ecdsa-sk</c> or <c>ed25519-sk</c>). SSH.NET cannot load them, and
/// the key formats belong to Core, so a module reaches the parser through this.
/// </summary>
public interface ISshSecurityKeyReader
{
	/// <summary>
	/// Parses <paramref name="privateKeyText"/> (UTF-8), decrypting its private section with <paramref name="passphrase"/>
	/// when it is protected. Never throws for bad input: the result says what was wrong.
	/// </summary>
	SecurityKeyFileResult Read(ReadOnlySpan<byte> privateKeyText, string? passphrase);
}
