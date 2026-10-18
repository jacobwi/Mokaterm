namespace Mokaterm.Abstractions.Import;

/// <summary>What the import can do with the key file a login names.</summary>
public enum ImportedKeyStatus
{
	/// <summary>The key can be copied into the keychain.</summary>
	Ready,

	/// <summary>The keychain already holds the same public key, so the login uses that entry and nothing new is added.</summary>
	InKeychain,

	/// <summary>No file at the path, or a path the import could not work out.</summary>
	Missing,

	/// <summary>The file is there but could not be used: no permission, too large, damaged, or a MAC that does not match.</summary>
	Unreadable,

	/// <summary>A key format or key type the keychain does not take.</summary>
	Unsupported,
}
