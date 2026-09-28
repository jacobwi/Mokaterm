using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Transfers;

namespace Mokaterm.Sessions.Transfers;

/// <summary>
/// Runs the transfers of one UI scope in the order they were queued, up to the concurrency limit from
/// <see cref="FileTransferSettings.MaxConcurrentTransfers"/>. The limit is read each time a slot frees up, so a
/// settings change applies to the next start.
/// </summary>
internal sealed class TransferQueue : ITransferQueue, IDisposable
{
	/// <summary>Progress raises <see cref="Changed"/> at most this often; state changes raise it at once.</summary>
	internal static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

	private readonly ISettingsService _settings;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<TransferQueue> _logger;
	private readonly Lock _lock = new();
	private readonly List<TransferItem> _items = [];

	// Queued items in start order, each with the sequence it was queued under. An entry whose item was canceled, removed
	// or queued again since is skipped when it comes up, so starting the next transfer never searches the whole list.
	private readonly Queue<(TransferItem Item, long Sequence)> _waiting = new();

	// Built on the first read after a change. Copying the list on every enqueue made a large upload quadratic, and every
	// copy past about ten thousand items landed on the large object heap.
	private ITransferItem[]? _snapshot;
	private long _lastSequence;
	private int _running;
	private long _lastProgressTimestamp;
	private ITimer? _progressTimer;
	private bool _disposed;

	public TransferQueue(ISettingsService settings, TimeProvider timeProvider, ILogger<TransferQueue> logger)
	{
		_settings = settings;
		_timeProvider = timeProvider;
		_logger = logger;
	}

	public event Action? Changed;

	public IReadOnlyList<ITransferItem> Items
	{
		get
		{
			lock (_lock)
			{
				return _snapshot ??= Snapshot();
			}
		}
	}

	/// <summary>Items queued or running.</summary>
	public int ActiveCount
	{
		get
		{
			lock (_lock)
			{
				return _items.Count(item => item.State is TransferState.Queued or TransferState.Running);
			}
		}
	}

	public ITransferItem Enqueue(TransferRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(request.ExecuteAsync);
		TransferItem item;
		lock (_lock)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			item = new TransferItem(request, _timeProvider.GetUtcNow(), ++_lastSequence);
			_items.Add(item);
			_waiting.Enqueue((item, item.Sequence));
			_snapshot = null;
		}

		RaiseChanged();
		StartQueued();
		return item;
	}

	public void Cancel(Guid id)
	{
		CancellationTokenSource? running = null;
		bool canceledQueued = false;
		lock (_lock)
		{
			TransferItem? item = FindItem(id);
			if (item?.State == TransferState.Queued)
			{
				item.Finish(TransferState.Canceled, null, _timeProvider.GetUtcNow());
				canceledQueued = true;
			}
			else if (item?.State == TransferState.Running)
			{
				// The item turns Canceled when its work stops; until then it still holds its slot.
				running = item.Cancellation;
			}
		}

		CancelQuietly(running);
		if (canceledQueued)
		{
			RaiseChanged();
		}
	}

	public void Retry(Guid id)
	{
		lock (_lock)
		{
			TransferItem? item = FindItem(id);
			if (_disposed || item is null || item.State is not (TransferState.Failed or TransferState.Canceled))
			{
				return;
			}

			item.Requeue(_timeProvider.GetUtcNow(), ++_lastSequence);
			_waiting.Enqueue((item, item.Sequence));
		}

		RaiseChanged();
		StartQueued();
	}

	public void Remove(Guid id)
	{
		CancellationTokenSource? running;
		lock (_lock)
		{
			TransferItem? item = FindItem(id);
			if (item is null)
			{
				return;
			}

			running = item.State == TransferState.Running ? item.Cancellation : null;
			if (item.State == TransferState.Queued)
			{
				// Its place in the waiting line is skipped once it is no longer queued.
				item.Finish(TransferState.Canceled, null, _timeProvider.GetUtcNow());
			}

			_ = _items.Remove(item);
			_snapshot = null;
		}

		CancelQuietly(running);
		RaiseChanged();
	}

	public void ClearFinished()
	{
		int removed;
		lock (_lock)
		{
			removed = _items.RemoveAll(item => item.IsFinished);
			if (removed > 0)
			{
				_snapshot = null;
			}
		}

		if (removed > 0)
		{
			RaiseChanged();
		}
	}

	public void Dispose()
	{
		List<CancellationTokenSource> running = [];
		lock (_lock)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			DateTimeOffset now = _timeProvider.GetUtcNow();
			foreach (TransferItem item in _items)
			{
				if (item.State == TransferState.Queued)
				{
					item.Finish(TransferState.Canceled, null, now);
				}
				else if (item is { State: TransferState.Running, Cancellation: { } cancellation })
				{
					running.Add(cancellation);
				}
			}

			_progressTimer?.Dispose();
			_progressTimer = null;
		}

		foreach (CancellationTokenSource cancellation in running)
		{
			CancelQuietly(cancellation);
		}
	}

	private void StartQueued()
	{
		// Read outside the lock: the settings service is someone else's code.
		int limit = Math.Max(1, _settings.Get<FileTransferSettings>().MaxConcurrentTransfers);
		List<(TransferItem Item, int Run, CancellationToken CancellationToken)> started = [];
		lock (_lock)
		{
			while (!_disposed && _running < limit && NextQueued() is { } next)
			{
				CancellationToken cancellationToken = next.Start(_timeProvider.GetUtcNow(), _timeProvider.GetTimestamp());
				_running++;
				started.Add((next, next.Run, cancellationToken));
			}
		}

		if (started.Count == 0)
		{
			return;
		}

		RaiseChanged();
		foreach ((TransferItem item, int run, CancellationToken cancellationToken) in started)
		{
			_ = Task.Run(() => RunAsync(item, run, cancellationToken));
		}
	}

	private async Task RunAsync(TransferItem item, int run, CancellationToken cancellationToken)
	{
		TransferState state = TransferState.Completed;
		string? error = null;
		try
		{
			TransferContext context = new(
				new InlineProgress<long>(transferred => ReportProgress(item, run, transferred)),
				total => SetTotalBytes(item, run, total),
				cancellationToken);
			await item.Request.ExecuteAsync(context);
		}
		catch (Exception) when (cancellationToken.IsCancellationRequested)
		{
			state = TransferState.Canceled;
		}
		catch (OperationCanceledException)
		{
			// The work gave up on its own, as a download does when the user dismisses its save dialog.
			state = TransferState.Canceled;
		}
		catch (Exception ex)
		{
			state = TransferState.Failed;
			error = ex.Message;

			// The message names the remote path; it goes to the transfer list, and only its type to the log.
			_logger.LogWarning("Transfer {TransferId} failed: {Error}", item.Id, LogSafe.Describe(ex));
		}

		lock (_lock)
		{
			_running--;
			if (item.Run == run && item.State == TransferState.Running)
			{
				item.Finish(state, error, _timeProvider.GetUtcNow());
			}
		}

		RaiseChanged();
		StartQueued();
	}

	private void ReportProgress(TransferItem item, int run, long transferredBytes)
	{
		bool raise = false;
		lock (_lock)
		{
			if (_disposed || item.Run != run || item.State != TransferState.Running)
			{
				return;
			}

			long now = _timeProvider.GetTimestamp();
			item.RecordProgress(transferredBytes, now, _timeProvider);
			if (_progressTimer is not null)
			{
				return;
			}

			TimeSpan sinceLast = _timeProvider.GetElapsedTime(_lastProgressTimestamp, now);
			if (sinceLast >= ProgressInterval)
			{
				_lastProgressTimestamp = now;
				raise = true;
			}
			else
			{
				// A trailing raise, so the last progress before a stall still reaches the view.
				_progressTimer = _timeProvider.CreateTimer(_ => OnProgressTimer(), null, ProgressInterval - sinceLast, Timeout.InfiniteTimeSpan);
			}
		}

		if (raise)
		{
			RaiseChanged();
		}
	}

	private void OnProgressTimer()
	{
		lock (_lock)
		{
			_progressTimer?.Dispose();
			_progressTimer = null;
			if (_disposed)
			{
				return;
			}

			_lastProgressTimestamp = _timeProvider.GetTimestamp();
		}

		RaiseChanged();
	}

	private void SetTotalBytes(TransferItem item, int run, long totalBytes)
	{
		lock (_lock)
		{
			if (item.Run != run || item.State != TransferState.Running)
			{
				return;
			}

			item.SetTotalBytes(totalBytes);
		}

		RaiseChanged();
	}

	private void CancelQuietly(CancellationTokenSource? cancellation)
	{
		if (cancellation is null)
		{
			return;
		}

		try
		{
			cancellation.Cancel();
		}
		catch (ObjectDisposedException)
		{
			// The work finished and released its token in the meantime.
		}
		catch (AggregateException ex)
		{
			_logger.LogWarning(ex, "A transfer's cancellation callback threw.");
		}
	}

	private TransferItem? NextQueued()
	{
		while (_waiting.TryDequeue(out (TransferItem Item, long Sequence) next))
		{
			if (next.Item.State == TransferState.Queued && next.Item.Sequence == next.Sequence)
			{
				return next.Item;
			}
		}

		return null;
	}

	private TransferItem? FindItem(Guid id) => _items.Find(item => item.Id == id);

	/// <summary>Newest first. Call while holding the lock.</summary>
	private ITransferItem[] Snapshot()
	{
		ITransferItem[] snapshot = new ITransferItem[_items.Count];
		for (int i = 0; i < snapshot.Length; i++)
		{
			snapshot[i] = _items[_items.Count - 1 - i];
		}

		return snapshot;
	}

	private void RaiseChanged() => EventRaiser.Raise(Changed, _logger, nameof(Changed));
}
