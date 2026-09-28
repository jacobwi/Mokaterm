using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.UI.Settings.Pages;

/// <summary>
/// Base for pages that list data encrypted in the vault. Loads while the vault is unlocked, empties while it is locked,
/// and reloads when the vault status or the data changes anywhere.
/// </summary>
public abstract class VaultListPageBase<TItem> : ComponentBase, IDisposable
{
	private readonly CancellationTokenSource _lifetime = new();
	private ILogger? _logger;
	private int _loadVersion;
	private bool _disposed;

	[Inject]
	protected IVault Vault { get; set; } = default!;

	[Inject]
	private ILoggerFactory LoggerFactory { get; set; } = default!;

	/// <summary>The latest items. Every load assigns a new list, which is how MokaTable notices the change.</summary>
	protected IReadOnlyList<TItem> Items { get; private set; } = [];

	protected bool IsUnlocked => Vault.Status == VaultStatus.Unlocked;

	/// <summary>False until the first load finishes, so pages can hold back empty states instead of flashing them.</summary>
	protected bool IsLoaded { get; private set; }

	protected string? LoadError { get; private set; }

	/// <summary>Shown when loading fails for a reason other than the vault locking.</summary>
	protected abstract string LoadErrorMessage { get; }

	private ILogger PageLogger => _logger ??= LoggerFactory.CreateLogger(GetType());

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	/// <summary>
	/// Returns the items already in display order. MokaColumn registers after the table's first data pass, so a sort
	/// requested through the table's parameters cannot apply to data that is ready before the first render.
	/// </summary>
	protected abstract ValueTask<IReadOnlyList<TItem>> LoadItemsAsync(CancellationToken cancellationToken);

	protected override async Task OnInitializedAsync()
	{
		Vault.StatusChanged += OnVaultStatusChanged;
		await ReloadAsync();
	}

	/// <summary>Reloads on the renderer's context. Safe to call from service events raised on any thread.</summary>
	protected void RequestReload() => _ = InvokeAsync(async () =>
	{
		await ReloadAsync();
		StateHasChanged();
	});

	protected virtual void Dispose(bool disposing)
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		if (disposing)
		{
			Vault.StatusChanged -= OnVaultStatusChanged;
			_lifetime.Cancel();
			_lifetime.Dispose();
		}
	}

	private async Task ReloadAsync()
	{
		if (_disposed)
		{
			return;
		}

		int version = ++_loadVersion;
		if (!IsUnlocked)
		{
			Items = [];
			LoadError = null;
			IsLoaded = true;
			return;
		}

		try
		{
			IReadOnlyList<TItem> items = await LoadItemsAsync(_lifetime.Token);
			if (version == _loadVersion)
			{
				Items = items;
				LoadError = null;
			}
		}
		catch (OperationCanceledException) when (_disposed)
		{
			// The page went away while loading.
		}
		catch (VaultLockedException)
		{
			if (version == _loadVersion)
			{
				Items = [];
			}
		}
		catch (Exception ex)
		{
			PageLogger.LogError(ex, "Loading {Page} failed", GetType().Name);
			if (version == _loadVersion)
			{
				LoadError = LoadErrorMessage;
			}
		}
		finally
		{
			if (version == _loadVersion)
			{
				IsLoaded = true;
			}
		}
	}

	private void OnVaultStatusChanged(VaultStatus status) => RequestReload();
}
