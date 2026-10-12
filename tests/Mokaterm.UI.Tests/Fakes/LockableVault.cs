using Mokaterm.Abstractions.Security;

namespace Mokaterm.UI.Tests.Fakes;

/// <summary>Unlocked until a test locks it, which it says the way the real vault does.</summary>
internal sealed class LockableVault : IVault
{
	public event Action<VaultStatus>? StatusChanged;

	public VaultStatus Status { get; private set; } = VaultStatus.Unlocked;

	public LockReason? LastLockReason { get; private set; }

	public bool IsDeviceUnlockAvailable => false;

	public bool IsDeviceUnlockEnabled => false;

	public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task CreateAsync(string masterPassword, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	public Task<UnlockResult> UnlockAsync(string masterPassword, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	public Task<UnlockResult> UnlockWithDeviceAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

	public Task SetDeviceUnlockAsync(bool enabled, CancellationToken cancellationToken = default) => throw new NotSupportedException();

	public Task<UnlockResult> ChangeMasterPasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public void Lock(LockReason reason = LockReason.User)
	{
		Status = VaultStatus.Locked;
		LastLockReason = reason;
		StatusChanged?.Invoke(Status);
	}

	public void ReportActivity()
	{
	}

	public Task ResetAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
