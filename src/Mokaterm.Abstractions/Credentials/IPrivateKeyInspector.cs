namespace Mokaterm.Abstractions.Credentials;

/// <summary>
/// Reads private keys for validation and display. Registered by a module that understands key formats
/// (the SSH module); optional everywhere else.
/// </summary>
public interface IPrivateKeyInspector
{
	/// <summary>Parses <paramref name="privateKey"/>, decrypting it with <paramref name="passphrase"/> when needed.</summary>
	PrivateKeyInspection Inspect(string privateKey, string? passphrase);
}

public enum PrivateKeyStatus
{
	Valid,
	PassphraseRequired,
	WrongPassphrase,
	Unsupported,
	Invalid,
}

/// <summary>The outcome of reading a key. Algorithm and fingerprint are set only when the key is valid.</summary>
public sealed record PrivateKeyInspection(PrivateKeyStatus Status, string? Algorithm = null, string? Fingerprint = null, string? Error = null)
{
	public bool IsValid => Status == PrivateKeyStatus.Valid;
}
