namespace Mokaterm.Abstractions.Import;

/// <summary>What the import created and what it left behind.</summary>
public sealed record ImportResult
{
	public int FoldersCreated { get; init; }

	public int HostsCreated { get; init; }

	/// <summary>Hosts that were already saved and took the new logins instead of being created again.</summary>
	public int HostsReused { get; init; }

	public int LoginsCreated { get; init; }

	/// <summary>Keys copied into the keychain. Several logins naming one key, or a key already there, add nothing.</summary>
	public int KeysAdded { get; init; }

	/// <summary>Logins set to sign in with a key from the keychain, whether that key is new or was already there.</summary>
	public int LoginsWithKeys { get; init; }

	public IReadOnlyList<ImportSkip> Skipped { get; init; } = [];

	/// <summary>Logins that were imported without their key, with the reason; they ask for a password instead.</summary>
	public IReadOnlyList<ImportSkip> KeysNotAdded { get; init; } = [];

	/// <summary>Set when the import stopped early; anything already created stays.</summary>
	public string? Error { get; init; }

	public bool CreatedAnything => FoldersCreated > 0 || HostsCreated > 0 || LoginsCreated > 0;
}
