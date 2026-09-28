using Mokaterm.Abstractions.Security;

namespace Mokaterm.Core.Storage;

/// <summary>
/// One decrypted vault document cached for a UI scope. The cache is dropped when the vault leaves Unlocked and when
/// another scope changes the document; updates run under a cross-scope lock against fresh data.
/// </summary>
internal sealed class VaultDocumentCache<TDocument> : IDisposable where TDocument : class
{
	private readonly string _name;
	private readonly Func<TDocument> _createEmpty;
	private readonly VaultDataStore _store;
	private readonly IVault _vault;
	private readonly VaultDocumentUpdateLocks _updateLocks;
	private readonly Lock _sync = new();
	private TDocument? _cached;

	// Bumped whenever the cache is dropped or replaced, so a load that started earlier cannot store stale data.
	private long _generation;

	public VaultDocumentCache(string name, Func<TDocument> createEmpty, VaultDataStore store, IVault vault, VaultDocumentUpdateLocks updateLocks)
	{
		_name = name;
		_createEmpty = createEmpty;
		_store = store;
		_vault = vault;
		_updateLocks = updateLocks;
		_vault.StatusChanged += OnVaultStatusChanged;
		_store.ExternalDocumentChanged += OnExternalDocumentChanged;
	}

	/// <summary>Raised after another UI scope changed the document. Handlers may run on any thread.</summary>
	public event Action? ExternalChange;

	/// <exception cref="VaultLockedException">The vault is locked.</exception>
	public async ValueTask<TDocument> GetAsync(CancellationToken cancellationToken)
	{
		if (_vault.Status != VaultStatus.Unlocked)
		{
			throw new VaultLockedException();
		}

		long generation;
		lock (_sync)
		{
			if (_cached is not null)
			{
				return _cached;
			}

			generation = _generation;
		}

		TDocument loaded = await LoadAsync(cancellationToken);
		lock (_sync)
		{
			if (_generation == generation)
			{
				_cached = loaded;
			}
		}

		return loaded;
	}

	/// <summary>
	/// Applies <paramref name="update"/> to the current document and writes the result unless it returns a null
	/// document. Validation exceptions thrown by <paramref name="update"/> leave the document untouched.
	/// </summary>
	public async Task<TResult> UpdateAsync<TResult>(Func<TDocument, (TDocument? Document, TResult Result)> update, CancellationToken cancellationToken)
	{
		using (await _updateLocks.AcquireAsync(_name, cancellationToken))
		{
			// The file, not the cache: a second copy of the desktop app writes the same file without reaching this
			// process's change events, and writing on top of a copy cached before that would silently drop its change.
			TDocument current = await LoadAsync(cancellationToken);
			(TDocument? next, TResult result) = update(current);
			if (next is not null)
			{
				await _store.WriteAsync(_name, next, cancellationToken);
			}

			lock (_sync)
			{
				_generation++;
				_cached = _vault.Status == VaultStatus.Unlocked ? next ?? current : null;
			}

			return result;
		}
	}

	public void Dispose()
	{
		_vault.StatusChanged -= OnVaultStatusChanged;
		_store.ExternalDocumentChanged -= OnExternalDocumentChanged;
	}

	/// <exception cref="VaultLockedException">The vault is locked.</exception>
	private async ValueTask<TDocument> LoadAsync(CancellationToken cancellationToken)
	{
		if (_vault.Status != VaultStatus.Unlocked)
		{
			throw new VaultLockedException();
		}

		return await _store.ReadAsync<TDocument>(_name, cancellationToken) ?? _createEmpty();
	}

	private void Drop()
	{
		lock (_sync)
		{
			_generation++;
			_cached = null;
		}
	}

	private void OnVaultStatusChanged(VaultStatus status)
	{
		if (status != VaultStatus.Unlocked)
		{
			Drop();
		}
	}

	private void OnExternalDocumentChanged(string name)
	{
		if (name == _name)
		{
			Drop();
			ExternalChange?.Invoke();
		}
	}
}
