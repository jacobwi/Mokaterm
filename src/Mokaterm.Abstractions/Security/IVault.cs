namespace Mokaterm.Abstractions.Security;

/// <summary>
/// The master-password vault that encrypts connections, credentials and known hosts. One instance per UI
/// scope: a Blazor Server circuit or a MAUI window. The data key only exists in memory while unlocked.
/// </summary>
public interface IVault
{
	VaultStatus Status { get; }

	/// <summary>Raised on every status change. Handlers may run on any thread.</summary>
	event Action<VaultStatus>? StatusChanged;

	/// <summary>Why the vault last locked. Lets the lock screen say "Locked after 15 minutes idle".</summary>
	LockReason? LastLockReason { get; }

	/// <summary>True when this platform can protect a device key (for example DPAPI on Windows).</summary>
	bool IsDeviceUnlockAvailable { get; }

	/// <summary>True when the vault key is also stored under the device key so it can unlock without the password.</summary>
	bool IsDeviceUnlockEnabled { get; }

	/// <summary>Reads the vault header to find out whether a vault exists. Call once before showing any UI.</summary>
	Task LoadAsync(CancellationToken cancellationToken = default);

	/// <summary>Creates a new vault protected by <paramref name="masterPassword"/> and leaves it unlocked.</summary>
	/// <exception cref="InvalidOperationException">A vault already exists.</exception>
	/// <exception cref="ArgumentException">The password does not meet <see cref="VaultPasswordPolicy"/>.</exception>
	Task CreateAsync(string masterPassword, CancellationToken cancellationToken = default);

	Task<UnlockResult> UnlockAsync(string masterPassword, CancellationToken cancellationToken = default);

	Task<UnlockResult> UnlockWithDeviceAsync(CancellationToken cancellationToken = default);

	/// <summary>Turns device unlock on or off. Requires an unlocked vault.</summary>
	Task SetDeviceUnlockAsync(bool enabled, CancellationToken cancellationToken = default);

	/// <summary>Re-wraps the data key under a new password. Encrypted data is not rewritten.</summary>
	/// <returns>
	/// <see cref="UnlockStatus.Success"/>, <see cref="UnlockStatus.InvalidPassword"/> when <paramref name="currentPassword"/>
	/// is wrong, or <see cref="UnlockStatus.Throttled"/> with the wait: the current password is a guess like any unlock
	/// attempt, so it shares the unlock throttle and is not even checked while that holds.
	/// </returns>
	/// <exception cref="ArgumentException">The new password does not meet <see cref="VaultPasswordPolicy"/>.</exception>
	Task<UnlockResult> ChangeMasterPasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default);

	/// <summary>Wipes the data key from memory. Open sessions keep running; the UI hides them until unlock.</summary>
	void Lock(LockReason reason = LockReason.User);

	/// <summary>Resets the idle auto-lock timer. Cheap enough to call from input handlers.</summary>
	void ReportActivity();

	/// <summary>
	/// Deletes the vault and every encrypted document. The only way out of a forgotten master password.
	/// Settings survive.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// On the web host the vault is not unlocked. Anyone who reaches that page sees its lock screen, so there a forgotten
	/// password is for the server's operator to clear; the message says so and can be shown as it is.
	/// </exception>
	Task ResetAsync(CancellationToken cancellationToken = default);
}
