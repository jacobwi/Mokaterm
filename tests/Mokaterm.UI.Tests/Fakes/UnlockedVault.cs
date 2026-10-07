using Mokaterm.Abstractions.Security;

namespace Mokaterm.UI.Tests.Fakes;

/// <summary>Unlocked, so a catalog loads and a view renders; nothing else is asked of it.</summary>
internal sealed class UnlockedVault : IVault
{
	public event Action<VaultStatus>? StatusChanged
	{
		add { }
		remove { }
	}

	public VaultStatus Status => VaultStatus.Unlocked;

	public LockReason? LastLockReason => null;

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
	}

	public void ReportActivity()
	{
	}

	public Task ResetAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
