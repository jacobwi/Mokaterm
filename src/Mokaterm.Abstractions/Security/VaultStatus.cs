namespace Mokaterm.Abstractions.Security;

public enum VaultStatus
{
	/// <summary><see cref="IVault.LoadAsync"/> has not run yet.</summary>
	Unknown,

	/// <summary>First run: no vault exists and the user must choose a master password.</summary>
	Uninitialized,

	Locked,

	Unlocked,
}

public enum LockReason
{
	User,
	Idle,
	Background,
	Shutdown,
	Reset,
}

public enum UnlockStatus
{
	Success,
	InvalidPassword,

	/// <summary>Too many failures; see <see cref="UnlockResult.RetryAfter"/>.</summary>
	Throttled,

	DeviceUnlockUnavailable,
	VaultMissing,

	/// <summary>The header could not be read. The vault file is damaged or from a newer version.</summary>
	Corrupted,
}

public readonly record struct UnlockResult(UnlockStatus Status, TimeSpan RetryAfter = default)
{
	public bool Succeeded => Status == UnlockStatus.Success;

	public static UnlockResult Success { get; } = new(UnlockStatus.Success);
}
