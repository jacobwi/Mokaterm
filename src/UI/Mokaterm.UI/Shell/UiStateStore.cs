using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Storage;

namespace Mokaterm.UI.Shell;

/// <summary>Loads and saves <see cref="UiStateDocument"/>. Saves are debounced so a burst of layout changes writes once.</summary>
internal sealed class UiStateStore : IAsyncDisposable
{
	private const string DocumentName = "ui-state";

	private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(750);

	private readonly IAppDataStore _store;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<UiStateStore> _logger;
	private readonly Lock _gate = new();
	private readonly SemaphoreSlim _writeLock = new(1, 1);
	private UiStateDocument _current = new();
	private ITimer? _saveTimer;
	private int _version;
	private int _savedVersion;
	private bool _disposed;

	public UiStateStore(IAppDataStore store, TimeProvider timeProvider, ILogger<UiStateStore> logger)
	{
		_store = store;
		_timeProvider = timeProvider;
		_logger = logger;
	}

	public UiStateDocument Current
	{
		get
		{
			lock (_gate)
			{
				return _current;
			}
		}
	}

	public async Task LoadAsync(CancellationToken cancellationToken)
	{
		UiStateDocument document;
		try
		{
			document = await _store.ReadAsync<UiStateDocument>(DocumentName, cancellationToken) ?? new UiStateDocument();
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// A damaged layout file only costs the saved layout; it must not keep the app from starting.
			_logger.LogWarning(ex, "Could not read the UI state document. Using the default layout.");
			document = new UiStateDocument();
		}

		lock (_gate)
		{
			_current = document;
		}
	}

	public void Update(Func<UiStateDocument, UiStateDocument> update)
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			UiStateDocument next = update(_current);
			if (next == _current)
			{
				return;
			}

			_current = next;
			_version++;
			_saveTimer ??= _timeProvider.CreateTimer(_ => _ = SaveAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
			_saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
		}
	}

	public async ValueTask DisposeAsync()
	{
		ITimer? timer;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			timer = _saveTimer;
			_saveTimer = null;
		}

		if (timer is not null)
		{
			await timer.DisposeAsync();
		}

		// The semaphore is not disposed: a save started by the last timer tick may still be waiting on it, and it
		// never allocates a wait handle, so there is nothing to release.
		await SaveAsync();
	}

	private async Task SaveAsync()
	{
		await _writeLock.WaitAsync();
		try
		{
			UiStateDocument snapshot;
			int version;
			lock (_gate)
			{
				if (_version == _savedVersion)
				{
					return;
				}

				snapshot = _current;
				version = _version;
			}

			await _store.WriteAsync(DocumentName, snapshot);
			lock (_gate)
			{
				_savedVersion = version;
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Could not save the UI state document.");
		}
		finally
		{
			_writeLock.Release();
		}
	}
}
