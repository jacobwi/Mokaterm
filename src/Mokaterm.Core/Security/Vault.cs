using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Core.Security;

/// <summary>
/// The master-password vault for one UI scope. The data key lives in pinned memory only while unlocked. Other scopes
/// follow creation, password changes and resets through <see cref="VaultHeaderStore.Changed"/>.
/// </summary>
internal sealed class Vault : IVault, IVaultCipher, IDisposable
{
	private readonly VaultHeaderStore _headerStore;
	private readonly UnlockThrottle _throttle;
	private readonly ISettingsService _settings;
	private readonly VaultOptions _options;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<Vault> _logger;
	private readonly IAppEnvironment _environment;
	private readonly IDeviceKeyProtector? _deviceKeyProtector;
	private readonly Guid _scopeId = Guid.NewGuid();

	// Guards the key, status and header. Cipher operations hold it too, so Lock cannot wipe the key mid-operation.
	private readonly Lock _state = new();

	// One create, unlock, password change, device toggle or reset at a time within this scope.
	private readonly SemaphoreSlim _operations = new(1, 1);
	private readonly ITimer _idleTimer;
	private PinnedBytes? _dataKey;
	private VaultStatus _status;
	private VaultHeader? _header;
	private long _headerVersion;
	private LockReason? _lastLockReason;
	private long _lastActivityTimestamp;
	private bool _disposed;

	public Vault(
		VaultHeaderStore headerStore,
		UnlockThrottle throttle,
		ISettingsService settings,
		VaultOptions options,
		TimeProvider timeProvider,
		ILogger<Vault> logger,
		IAppEnvironment environment,
		IDeviceKeyProtector? deviceKeyProtector = null)
	{
		_headerStore = headerStore;
		_throttle = throttle;
		_settings = settings;
		_options = options;
		_timeProvider = timeProvider;
		_logger = logger;
		_environment = environment;
		_deviceKeyProtector = deviceKeyProtector;
		_lastActivityTimestamp = timeProvider.GetTimestamp();
		_headerStore.Changed += OnHeaderChanged;
		_idleTimer = timeProvider.CreateTimer(_ => CheckIdle(), null, options.IdleCheckInterval, options.IdleCheckInterval);
	}

	public event Action<VaultStatus>? StatusChanged;

	public VaultStatus Status
	{
		get
		{
			lock (_state)
			{
				return _status;
			}
		}
	}

	public LockReason? LastLockReason
	{
		get
		{
			lock (_state)
			{
				return _lastLockReason;
			}
		}
	}

	public bool IsDeviceUnlockAvailable => _deviceKeyProtector is not null;

	public bool IsDeviceUnlockEnabled
	{
		get
		{
			lock (_state)
			{
				return _header?.Device is not null;
			}
		}
	}

	Guid IVaultCipher.VaultId
	{
		get
		{
			lock (_state)
			{
				return RequireUnlockedHeader().VaultId;
			}
		}
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		VaultHeaderSnapshot snapshot = await _headerStore.GetAsync(cancellationToken);
		ApplySnapshot(snapshot);
	}

	public async Task CreateAsync(string masterPassword, CancellationToken cancellationToken = default)
	{
		ThrowIfWeak(masterPassword, nameof(masterPassword));
		await _operations.WaitAsync(cancellationToken);
		PinnedBytes? dataKey = null;
		try
		{
			if ((await _headerStore.GetAsync(cancellationToken)).State != VaultHeaderState.Missing)
			{
				throw new InvalidOperationException("A vault already exists.");
			}

			Guid vaultId = Guid.NewGuid();
			VaultKdfParameters kdf = VaultKeyDerivation.CreateArgon2idParameters(_options);
			dataKey = VaultCrypto.CreateDataKey();
			VaultWrappedKey masterKey;
			using (PinnedBytes passwordKey = await VaultKeyDerivation.DeriveAsync(masterPassword, kdf, cancellationToken))
			{
				masterKey = VaultCrypto.WrapKey(passwordKey.Span, dataKey.Span, vaultId);
			}

			DateTimeOffset now = _timeProvider.GetUtcNow();
			VaultHeader header = new()
			{
				Format = VaultHeader.CurrentFormat,
				VaultId = vaultId,
				Kdf = kdf,
				MasterKey = masterKey,
				KeyCheck = VaultCrypto.ComputeKeyCheck(dataKey.Span),
				CreatedAt = now,
				UpdatedAt = now,
			};

			VaultHeaderSnapshot snapshot = await _headerStore.TryCreateAsync(header, _scopeId, cancellationToken)
				?? throw new InvalidOperationException("A vault already exists.");

			_throttle.Reset();
			bool entered = TryEnterUnlocked(snapshot, dataKey);
			dataKey = null;
			if (!entered)
			{
				throw new InvalidOperationException("The vault was reset in another window while it was being created.");
			}
		}
		finally
		{
			dataKey?.Dispose();
			_operations.Release();
		}
	}

	public async Task<UnlockResult> UnlockAsync(string masterPassword, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(masterPassword);
		await _operations.WaitAsync(cancellationToken);
		try
		{
			using UnlockThrottle.Turn turn = await _throttle.WaitTurnAsync(cancellationToken);
			if (!_throttle.TryBegin(out TimeSpan retryAfter))
			{
				return new UnlockResult(UnlockStatus.Throttled, retryAfter);
			}

			VaultHeaderSnapshot snapshot = await _headerStore.GetAsync(cancellationToken);
			if (snapshot is not { State: VaultHeaderState.Valid, Header: { } header })
			{
				return new UnlockResult(snapshot.State == VaultHeaderState.Missing ? UnlockStatus.VaultMissing : UnlockStatus.Corrupted);
			}

			PinnedBytes? dataKey = await UnwrapWithPasswordAsync(masterPassword, header, cancellationToken);
			if (dataKey is null)
			{
				_throttle.RecordFailure();
				return new UnlockResult(UnlockStatus.InvalidPassword);
			}

			if (!VaultCrypto.VerifyKeyCheck(dataKey.Span, header.KeyCheck))
			{
				dataKey.Dispose();
				_logger.LogWarning("The vault key unwrapped but its key check does not match the header");
				return new UnlockResult(UnlockStatus.Corrupted);
			}

			_throttle.Reset();
			return TryEnterUnlocked(snapshot, dataKey) ? UnlockResult.Success : new UnlockResult(UnlockStatus.VaultMissing);
		}
		finally
		{
			_operations.Release();
		}
	}

	public async Task<UnlockResult> UnlockWithDeviceAsync(CancellationToken cancellationToken = default)
	{
		if (_deviceKeyProtector is null)
		{
			return new UnlockResult(UnlockStatus.DeviceUnlockUnavailable);
		}

		await _operations.WaitAsync(cancellationToken);
		try
		{
			VaultHeaderSnapshot snapshot = await _headerStore.GetAsync(cancellationToken);
			if (snapshot is not { State: VaultHeaderState.Valid, Header: { } header })
			{
				return new UnlockResult(snapshot.State == VaultHeaderState.Missing ? UnlockStatus.VaultMissing : UnlockStatus.Corrupted);
			}

			if (header.Device is not { } device)
			{
				return new UnlockResult(UnlockStatus.DeviceUnlockUnavailable);
			}

			byte[] unprotected;
			try
			{
				unprotected = await _deviceKeyProtector.UnprotectAsync(device.ProtectedKey, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "The device key protector could not unprotect the vault key");
				return new UnlockResult(UnlockStatus.DeviceUnlockUnavailable);
			}

			PinnedBytes dataKey = PinnedBytes.Copy(unprotected);
			CryptographicOperations.ZeroMemory(unprotected);
			if (!VaultCrypto.VerifyKeyCheck(dataKey.Span, header.KeyCheck))
			{
				dataKey.Dispose();
				_logger.LogWarning("The device-protected vault key does not match the vault's key check");
				return new UnlockResult(UnlockStatus.DeviceUnlockUnavailable);
			}

			return TryEnterUnlocked(snapshot, dataKey) ? UnlockResult.Success : new UnlockResult(UnlockStatus.VaultMissing);
		}
		finally
		{
			_operations.Release();
		}
	}

	public async Task SetDeviceUnlockAsync(bool enabled, CancellationToken cancellationToken = default)
	{
		await _operations.WaitAsync(cancellationToken);
		try
		{
			Guid vaultId;
			byte[]? protectedKey = null;
			if (enabled)
			{
				if (_deviceKeyProtector is not { } protector)
				{
					throw new InvalidOperationException("This platform cannot protect a device key.");
				}

				PinnedBytes keyCopy;
				lock (_state)
				{
					vaultId = RequireUnlockedHeader().VaultId;
					keyCopy = PinnedBytes.Copy(RequireKey().Span);
				}

				using (keyCopy)
				{
					protectedKey = await protector.ProtectAsync(keyCopy.Array, cancellationToken);
				}
			}
			else
			{
				lock (_state)
				{
					vaultId = RequireUnlockedHeader().VaultId;
				}
			}

			VaultHeaderSnapshot? snapshot = await _headerStore.UpdateAsync(
				_scopeId,
				(header, _) =>
				{
					EnsureSameVault(header, vaultId);
					VaultHeader? replacement = protectedKey is not null
						? header with { Device = new VaultDeviceKey { ProtectedKey = protectedKey }, UpdatedAt = _timeProvider.GetUtcNow() }
						: header.Device is null ? null : header with { Device = null, UpdatedAt = _timeProvider.GetUtcNow() };
					return Task.FromResult(replacement);
				},
				cancellationToken);

			if (snapshot is not null)
			{
				ApplySnapshot(snapshot);
			}
		}
		finally
		{
			_operations.Release();
		}
	}

	public async Task<UnlockResult> ChangeMasterPasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(currentPassword);
		ThrowIfWeak(newPassword, nameof(newPassword));
		await _operations.WaitAsync(cancellationToken);
		try
		{
			Guid vaultId;
			lock (_state)
			{
				vaultId = RequireUnlockedHeader().VaultId;
			}

			// The current password is a guess like any unlock attempt, so it shares the throttle.
			using UnlockThrottle.Turn turn = await _throttle.WaitTurnAsync(cancellationToken);
			if (!_throttle.TryBegin(out TimeSpan retryAfter))
			{
				_logger.LogWarning("Master password change refused while password attempts are throttled");
				return new UnlockResult(UnlockStatus.Throttled, retryAfter);
			}

			bool verified = false;
			VaultHeaderSnapshot? snapshot = await _headerStore.UpdateAsync(
				_scopeId,
				async (header, token) =>
				{
					EnsureSameVault(header, vaultId);
					using PinnedBytes? dataKey = await UnwrapWithPasswordAsync(currentPassword, header, token);
					if (dataKey is null)
					{
						return null;
					}

					verified = true;
					VaultKdfParameters kdf = VaultKeyDerivation.CreateArgon2idParameters(_options);
					using PinnedBytes newPasswordKey = await VaultKeyDerivation.DeriveAsync(newPassword, kdf, token);
					return header with
					{
						Kdf = kdf,
						MasterKey = VaultCrypto.WrapKey(newPasswordKey.Span, dataKey.Span, header.VaultId),
						UpdatedAt = _timeProvider.GetUtcNow(),
					};
				},
				cancellationToken);

			if (!verified)
			{
				_throttle.RecordFailure();
				return new UnlockResult(UnlockStatus.InvalidPassword);
			}

			_throttle.Reset();
			if (snapshot is not null)
			{
				ApplySnapshot(snapshot);
			}

			return UnlockResult.Success;
		}
		finally
		{
			_operations.Release();
		}
	}

	public void Lock(LockReason reason = LockReason.User)
	{
		bool locked;
		lock (_state)
		{
			locked = _status == VaultStatus.Unlocked && TrySetStatus(VaultStatus.Locked);
			if (locked)
			{
				_lastLockReason = reason;
			}
		}

		if (locked)
		{
			StatusChanged?.Invoke(VaultStatus.Locked);
		}
	}

	public void ReportActivity() => Interlocked.Exchange(ref _lastActivityTimestamp, _timeProvider.GetTimestamp());

	public async Task ResetAsync(CancellationToken cancellationToken = default)
	{
		await _operations.WaitAsync(cancellationToken);
		try
		{
			// Every visitor of the web host gets its lock screen. A reset from there would let anyone who reaches the page
			// wipe every saved host and then claim the installation with a vault of their own.
			if (_environment.Kind == HostKind.Web && Status != VaultStatus.Unlocked)
			{
				throw new InvalidOperationException(
					"This server does not reset a locked vault. Unlock it and reset it from Settings, or ask whoever runs the server to delete vault.json and the vault folder in its data directory.");
			}

			VaultHeaderSnapshot snapshot = await _headerStore.DeleteAsync(_scopeId, cancellationToken);
			_throttle.Reset();
			ApplySnapshot(snapshot);
		}
		finally
		{
			_operations.Release();
		}
	}

	void IVaultCipher.Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData, Span<byte> destination)
	{
		lock (_state)
		{
			AesGcmBox.Seal(RequireKey().Span, plaintext, associatedData, destination);
		}
	}

	void IVaultCipher.Decrypt(ReadOnlySpan<byte> sealedBox, ReadOnlySpan<byte> associatedData, Span<byte> destination)
	{
		lock (_state)
		{
			AesGcmBox.Open(RequireKey().Span, sealedBox, associatedData, destination);
		}
	}

	public void Dispose()
	{
		lock (_state)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			if (_status == VaultStatus.Unlocked && TrySetStatus(VaultStatus.Locked))
			{
				_lastLockReason = LockReason.Shutdown;
			}
		}

		_headerStore.Changed -= OnHeaderChanged;
		_idleTimer.Dispose();

		// _operations is not disposed: an operation finishing after the scope ended would otherwise throw on Release.
	}

	private static void ThrowIfWeak(string password, string parameterName)
	{
		ArgumentNullException.ThrowIfNull(password, parameterName);
		if (!VaultPasswordPolicy.IsAcceptable(password))
		{
			throw new ArgumentException(VaultPasswordPolicy.Requirement, parameterName);
		}
	}

	private static void EnsureSameVault(VaultHeader header, Guid vaultId)
	{
		if (header.VaultId != vaultId)
		{
			throw new VaultLockedException("The vault was replaced in another window.");
		}
	}

	private static async Task<PinnedBytes?> UnwrapWithPasswordAsync(string password, VaultHeader header, CancellationToken cancellationToken)
	{
		if (password.Length == 0)
		{
			return null;
		}

		using PinnedBytes passwordKey = await VaultKeyDerivation.DeriveAsync(password, header.Kdf, cancellationToken);
		return VaultCrypto.TryUnwrapKey(passwordKey.Span, header.MasterKey, header.VaultId);
	}

	/// <summary>
	/// Takes ownership of <paramref name="dataKey"/>. Returns false, wiping the key, when another scope reset or replaced
	/// the vault while the key was being unwrapped.
	/// </summary>
	private bool TryEnterUnlocked(VaultHeaderSnapshot snapshot, PinnedBytes dataKey)
	{
		bool changed;
		lock (_state)
		{
			if (_disposed)
			{
				dataKey.Dispose();
				throw new ObjectDisposedException(nameof(Vault));
			}

			if (snapshot.Version >= _headerVersion)
			{
				_header = snapshot.Header;
				_headerVersion = snapshot.Version;
			}
			else if (_header?.VaultId != snapshot.Header?.VaultId)
			{
				dataKey.Dispose();
				return false;
			}

			WipeKey();
			_dataKey = dataKey;
			changed = _status != VaultStatus.Unlocked;
			_status = VaultStatus.Unlocked;
			Interlocked.Exchange(ref _lastActivityTimestamp, _timeProvider.GetTimestamp());
		}

		if (changed)
		{
			StatusChanged?.Invoke(VaultStatus.Unlocked);
		}

		return true;
	}

	private void OnHeaderChanged(VaultHeaderSnapshot snapshot, Guid originId)
	{
		// The originating scope applies its own change before the store returns.
		if (originId != _scopeId)
		{
			ApplySnapshot(snapshot);
		}
	}

	/// <summary>Moves to the status a header snapshot implies. Snapshots older than one already applied are ignored.</summary>
	private void ApplySnapshot(VaultHeaderSnapshot snapshot)
	{
		List<VaultStatus> transitions = [];
		lock (_state)
		{
			if (_disposed || snapshot.Version < _headerVersion)
			{
				return;
			}

			_headerVersion = snapshot.Version;
			Guid? previousVaultId = _header?.VaultId;
			_header = snapshot.Header;
			if (snapshot.State == VaultHeaderState.Missing)
			{
				if (_status is VaultStatus.Unlocked or VaultStatus.Locked)
				{
					_lastLockReason = LockReason.Reset;
				}

				if (_status == VaultStatus.Unlocked && TrySetStatus(VaultStatus.Locked))
				{
					transitions.Add(VaultStatus.Locked);
				}

				if (TrySetStatus(VaultStatus.Uninitialized))
				{
					transitions.Add(VaultStatus.Uninitialized);
				}
			}
			else if (_status == VaultStatus.Unlocked && previousVaultId != snapshot.Header?.VaultId)
			{
				_lastLockReason = LockReason.Reset;
				TrySetStatus(VaultStatus.Locked);
				transitions.Add(VaultStatus.Locked);
			}
			else if (_status is VaultStatus.Unknown or VaultStatus.Uninitialized)
			{
				// A damaged header still shows the lock screen; unlocking then reports Corrupted and offers a reset.
				TrySetStatus(VaultStatus.Locked);
				transitions.Add(VaultStatus.Locked);
			}
		}

		foreach (VaultStatus status in transitions)
		{
			StatusChanged?.Invoke(status);
		}
	}

	private void CheckIdle()
	{
		try
		{
			if (Status != VaultStatus.Unlocked)
			{
				return;
			}

			int minutes = _settings.Get<SecuritySettings>().AutoLockMinutes;
			if (minutes > 0 && _timeProvider.GetElapsedTime(Interlocked.Read(ref _lastActivityTimestamp)) >= TimeSpan.FromMinutes(minutes))
			{
				Lock(LockReason.Idle);
			}
		}
		catch (Exception ex)
		{
			// Timer callbacks run on the thread pool, where an escaping exception would end the process.
			_logger.LogError(ex, "The idle auto-lock check failed");
		}
	}

	/// <summary>Call while holding <see cref="_state"/>.</summary>
	private PinnedBytes RequireKey() => _dataKey ?? throw new VaultLockedException();

	/// <summary>Call while holding <see cref="_state"/>.</summary>
	private VaultHeader RequireUnlockedHeader() =>
		_dataKey is not null && _header is { } header ? header : throw new VaultLockedException();

	/// <summary>Call while holding <see cref="_state"/>. Leaving Unlocked always wipes the key.</summary>
	private bool TrySetStatus(VaultStatus status)
	{
		if (_status == status)
		{
			return false;
		}

		if (_status == VaultStatus.Unlocked)
		{
			WipeKey();
		}

		_status = status;
		return true;
	}

	/// <summary>Call while holding <see cref="_state"/>.</summary>
	private void WipeKey()
	{
		_dataKey?.Dispose();
		_dataKey = null;
	}
}
