namespace Mokaterm.Abstractions.Import;

/// <summary>
/// What adding the key file a login names to the keychain would do, as the importer's preview found it. Nothing here is
/// secret: the key itself is read again when the import runs and goes straight into the vault.
/// </summary>
public sealed record ImportedKeyFile
{
	public required ImportedKeyStatus Status { get; init; }

	/// <summary>The full path the key was looked for at; null when the path could not be worked out.</summary>
	public string? Path { get; init; }

	/// <summary>Why the key cannot be added, as a finished sentence. Null while it can.</summary>
	public string? Reason { get; init; }

	/// <summary>Key type such as <c>ssh-ed25519</c>, when the file says.</summary>
	public string? Algorithm { get; init; }

	/// <summary>The <c>SHA256:</c> fingerprint of the public key, when it can be read without the passphrase.</summary>
	public string? Fingerprint { get; init; }

	/// <summary>The key is protected by a passphrase, which is asked for each time a login uses it.</summary>
	public bool NeedsPassphrase { get; init; }

	/// <summary>The keychain entry holding the same key, for <see cref="ImportedKeyStatus.InKeychain"/>.</summary>
	public string? KeychainName { get; init; }

	/// <summary>True when the key can end up on the login, whether it is new to the keychain or already there.</summary>
	public bool IsUsable => Status is ImportedKeyStatus.Ready or ImportedKeyStatus.InKeychain;
}
