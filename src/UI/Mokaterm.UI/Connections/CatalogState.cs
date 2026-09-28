using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.UI.Connections;

/// <summary>
/// The connection catalog shared by the explorer, the welcome view, editors and palette commands, reloaded when the
/// repository changes. Cleared as soon as the vault locks so decrypted names and addresses do not linger.
/// </summary>
internal sealed class CatalogState : IDisposable
{
	private readonly IConnectionRepository _repository;
	private readonly IVault _vault;
	private readonly ILogger<CatalogState> _logger;
	private readonly Lock _gate = new();
	private Task? _pendingLoad;
	private int _version;

	public CatalogState(IConnectionRepository repository, IVault vault, ILogger<CatalogState> logger)
	{
		_repository = repository;
		_vault = vault;
		_logger = logger;
		_repository.Changed += OnRepositoryChanged;
		_vault.StatusChanged += OnVaultStatusChanged;
	}

	/// <summary>Raised after the catalog was reloaded or cleared. Any thread.</summary>
	public event Action? Changed;

	public ConnectionCatalog Catalog { get; private set; } = ConnectionCatalog.Empty;

	public bool IsLoaded { get; private set; }

	/// <summary>Loads the catalog unless it is already loaded; concurrent callers share one load.</summary>
	public Task EnsureLoadedAsync()
	{
		if (IsLoaded)
		{
			return Task.CompletedTask;
		}

		lock (_gate)
		{
			if (_pendingLoad is null || _pendingLoad.IsCompleted)
			{
				_pendingLoad = RefreshAsync();
			}

			return _pendingLoad;
		}
	}

	public async Task RefreshAsync()
	{
		int version = Interlocked.Increment(ref _version);
		ConnectionCatalog catalog;
		try
		{
			catalog = await _repository.GetCatalogAsync();
		}
		catch (VaultLockedException)
		{
			return;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogError(ex, "Loading the connection catalog failed.");
			return;
		}

		// A newer refresh or a lock happened while this one was loading; its result wins.
		if (version != Volatile.Read(ref _version))
		{
			return;
		}

		Catalog = catalog;
		IsLoaded = true;
		Changed?.Invoke();
	}

	public void Dispose()
	{
		_repository.Changed -= OnRepositoryChanged;
		_vault.StatusChanged -= OnVaultStatusChanged;
	}

	private void OnRepositoryChanged()
	{
		if (_vault.Status == VaultStatus.Unlocked)
		{
			_ = RefreshAsync();
		}
	}

	private void OnVaultStatusChanged(VaultStatus status)
	{
		if (status == VaultStatus.Unlocked)
		{
			return;
		}

		Interlocked.Increment(ref _version);
		Catalog = ConnectionCatalog.Empty;
		IsLoaded = false;
		Changed?.Invoke();
	}
}
