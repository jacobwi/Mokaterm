namespace Mokaterm.Core.Security;

internal enum VaultHeaderState
{
	Missing,
	Valid,

	/// <summary>The file exists but cannot be read, or it was written by a newer version.</summary>
	Corrupted,
}

/// <summary>
/// The header as <see cref="VaultHeaderStore"/> last saw it. <see cref="Version"/> grows with every load, write and
/// delete, so a scope can ignore a snapshot older than one it already applied.
/// </summary>
internal sealed record VaultHeaderSnapshot(VaultHeaderState State, VaultHeader? Header, long Version);
