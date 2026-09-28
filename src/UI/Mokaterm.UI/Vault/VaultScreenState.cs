namespace Mokaterm.UI.Vault;

/// <summary>
/// Remembers that the automatic device unlock already ran in this scope. It runs once when the app opens; after the
/// user or the idle timer locks the vault, unlocking straight back without asking would defeat the lock.
/// </summary>
internal sealed class VaultScreenState
{
	private int _automaticDeviceUnlockClaimed;

	public bool TryClaimAutomaticDeviceUnlock() => Interlocked.Exchange(ref _automaticDeviceUnlockClaimed, 1) == 0;
}
