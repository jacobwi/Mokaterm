using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Transfers;
using Mokaterm.Sessions.Tests.Fakes;
using Mokaterm.Sessions.Transfers;

namespace Mokaterm.Sessions.Tests;

public sealed class TransferQueueTests : IDisposable
{
	private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 17, 8, 0, 0, TimeSpan.Zero));
	private readonly UncachedSettingsService _settings = new();
	private readonly TransferQueue _queue;

	public TransferQueueTests() => _queue = new TransferQueue(_settings, _time, NullLogger<TransferQueue>.Instance);

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Enqueue_RunsUpToTheLimit_InQueueOrder()
	{
		_settings.Set(new FileTransferSettings { MaxConcurrentTransfers = 2 });
		string[] names = ["a", "b", "c", "d"];
		ConcurrentQueue<string> started = new();
		Dictionary<string, TaskCompletionSource> gates = [];
		List<ITransferItem> items = [];
		foreach (string name in names)
		{
			TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
			gates[name] = gate;
			items.Add(_queue.Enqueue(Request(name, async context =>
			{
				started.Enqueue(name);
				await gate.Task.WaitAsync(context.CancellationToken);
			})));
		}

		await Eventually.TrueAsync(() => started.Count == 2, "two transfers start");
		await Eventually.StaysTrueAsync(() => started.Count == 2 && _queue.ActiveCount == 4, "the others wait for a slot");
		Assert.Equal(TransferState.Queued, items[2].State);
		Assert.Equal(TransferState.Queued, items[3].State);
		Assert.Equal(Enumerable.Reverse(items), _queue.Items);

		gates["a"].SetResult();
		await Eventually.TrueAsync(() => started.Count == 3, "a freed slot starts the next transfer");
		Assert.Equal("c", started.ToArray()[2]);

		foreach (TaskCompletionSource gate in gates.Values)
		{
			_ = gate.TrySetResult();
		}

		await Eventually.TrueAsync(() => items.TrueForAll(item => item.State == TransferState.Completed), "every transfer completes");

		// The first two share the concurrency limit and run on separate pool threads, so either may begin first.
		string[] order = started.ToArray();
		Assert.Equal(["a", "b"], order.Take(2).Order(StringComparer.Ordinal));
		Assert.Equal(["c", "d"], order.Skip(2));
		Assert.Equal(0, _queue.ActiveCount);
	}

	[Fact]
	public async Task Cancel_QueuedItem_IsCanceledAtOnce_AndNeverRuns()
	{
		_settings.Set(new FileTransferSettings { MaxConcurrentTransfers = 1 });
		TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
		int queuedRuns = 0;
		ITransferItem running = _queue.Enqueue(Request("running", context => gate.Task.WaitAsync(context.CancellationToken)));
		ITransferItem queued = _queue.Enqueue(Request("queued", context =>
		{
			_ = Interlocked.Increment(ref queuedRuns);
			return Task.CompletedTask;
		}));
		await Eventually.TrueAsync(() => running.State == TransferState.Running, "the first transfer runs");

		_queue.Cancel(queued.Id);

		Assert.Equal(TransferState.Canceled, queued.State);
		Assert.Equal(_time.GetUtcNow(), queued.FinishedAt);
		gate.SetResult();
		await Eventually.TrueAsync(() => running.State == TransferState.Completed, "the first transfer completes");
		await Eventually.StaysTrueAsync(() => Volatile.Read(ref queuedRuns) == 0, "the canceled transfer never runs");
	}

	[Fact]
	public async Task Cancel_RunningItem_CancelsItsWork()
	{
		ITransferItem item = _queue.Enqueue(Request("big.iso", context => Task.Delay(Timeout.Infinite, context.CancellationToken)));
		await Eventually.TrueAsync(() => item.State == TransferState.Running, "the transfer runs");

		_queue.Cancel(item.Id);

		await Eventually.TrueAsync(() => item.State == TransferState.Canceled, "the work stops and the item is canceled");
		Assert.Equal(0, _queue.ActiveCount);
		Assert.Null(item.Error);
	}

	[Fact]
	public async Task WorkThatThrowsCancellation_EndsCanceled_EvenWhenTheQueueDidNotCancelIt()
	{
		// What a transfer does when the user dismisses its save dialog.
		ITransferItem item = _queue.Enqueue(Request("report.pdf", _ => Task.FromException(new OperationCanceledException("The download was cancelled."))));

		await Eventually.TrueAsync(() => item.State is TransferState.Canceled or TransferState.Failed, "the transfer ends");

		Assert.Equal(TransferState.Canceled, item.State);
		Assert.Null(item.Error);
	}

	[Fact]
	public async Task Retry_FailedItem_ResetsProgressAndRunsAgain()
	{
		int runs = 0;
		ITransferItem item = _queue.Enqueue(Request("log.txt", context =>
		{
			if (Interlocked.Increment(ref runs) == 1)
			{
				context.Progress.Report(500);
				throw new IOException("No space left on device.");
			}

			return Task.CompletedTask;
		}));

		await Eventually.TrueAsync(() => item.State == TransferState.Failed, "the first run fails");
		Assert.Equal("No space left on device.", item.Error);
		Assert.Equal(500, item.TransferredBytes);

		_queue.Retry(item.Id);

		Assert.Null(item.Error);
		Assert.Equal(0, item.TransferredBytes);
		await Eventually.TrueAsync(() => item.State == TransferState.Completed, "the retry completes");
		Assert.Equal(2, Volatile.Read(ref runs));
	}

	[Fact]
	public async Task Progress_RaisesChangedAtMostEveryQuarterSecond_WithATrailingUpdate()
	{
		Channel<long> reports = Channel.CreateUnbounded<long>();
		int changes = 0;
		_queue.Changed += () => Interlocked.Increment(ref changes);
		ITransferItem item = _queue.Enqueue(Request("data.bin", context => ReportAllAsync(reports, context)));
		await Eventually.TrueAsync(() => item.State == TransferState.Running, "the transfer runs");
		long before = await Eventually.StableAsync(() => Volatile.Read(ref changes));

		for (long bytes = 10; bytes <= 1000; bytes += 10)
		{
			_ = reports.Writer.TryWrite(bytes);
		}

		await Eventually.TrueAsync(() => item.TransferredBytes == 1000, "every report is recorded");
		await Eventually.StaysTrueAsync(() => Volatile.Read(ref changes) == before + 1, "a burst of progress raises Changed once");

		_time.Advance(TransferQueue.ProgressInterval);
		await Eventually.TrueAsync(() => Volatile.Read(ref changes) == before + 2, "the last progress of the burst follows after the interval");

		reports.Writer.Complete();
		await Eventually.TrueAsync(() => item.State == TransferState.Completed, "the transfer completes");
		await Eventually.TrueAsync(() => Volatile.Read(ref changes) >= before + 3, "completion raises Changed at once");
	}

	[Fact]
	public async Task Progress_SmoothsTheRate_AndResetsItWhenFinished()
	{
		Channel<long> reports = Channel.CreateUnbounded<long>();
		ITransferItem item = _queue.Enqueue(Request("video.mp4", context =>
		{
			context.SetTotalBytes(8000);
			return ReportAllAsync(reports, context);
		}));
		await Eventually.TrueAsync(() => item.State == TransferState.Running && item.TotalBytes == 8000, "the transfer runs with a known size");

		_time.Advance(TimeSpan.FromSeconds(1));
		await ReportAsync(1000);
		Assert.Equal(1000, item.BytesPerSecond, precision: 3);

		// 3000 bytes in the next second: the smoothed rate moves toward it without jumping there.
		_time.Advance(TimeSpan.FromSeconds(1));
		await ReportAsync(4000);
		Assert.InRange(item.BytesPerSecond, 1001, 2999);

		reports.Writer.Complete();
		await Eventually.TrueAsync(() => item.State == TransferState.Completed, "the transfer completes");
		Assert.Equal(0, item.BytesPerSecond);

		async Task ReportAsync(long bytes)
		{
			_ = reports.Writer.TryWrite(bytes);
			await Eventually.TrueAsync(() => item.TransferredBytes == bytes, $"progress reaches {bytes} bytes");
		}
	}

	[Fact]
	public async Task ClearFinished_RemovesOnlyFinishedItems()
	{
		_settings.Set(new FileTransferSettings { MaxConcurrentTransfers = 4 });
		ITransferItem completed = _queue.Enqueue(Request("done", _ => Task.CompletedTask));
		ITransferItem failed = _queue.Enqueue(Request("broken", _ => Task.FromException(new IOException("Permission denied."))));
		ITransferItem running = _queue.Enqueue(Request("slow", context => Task.Delay(Timeout.Infinite, context.CancellationToken)));
		ITransferItem canceled = _queue.Enqueue(Request("dropped", context => Task.Delay(Timeout.Infinite, context.CancellationToken)));
		_queue.Cancel(canceled.Id);
		await Eventually.TrueAsync(
			() => completed.State == TransferState.Completed
				&& failed.State == TransferState.Failed
				&& running.State == TransferState.Running
				&& canceled.State == TransferState.Canceled,
			"each transfer reaches its state");
		int changes = 0;
		_queue.Changed += () => Interlocked.Increment(ref changes);

		_queue.ClearFinished();

		Assert.Same(running, Assert.Single(_queue.Items));
		Assert.True(Volatile.Read(ref changes) >= 1);
	}

	[Fact]
	public async Task Remove_RunningItem_CancelsIt_AndFreesTheSlot()
	{
		_settings.Set(new FileTransferSettings { MaxConcurrentTransfers = 1 });
		TaskCompletionSource cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
		ITransferItem first = _queue.Enqueue(Request("first", async context =>
		{
			try
			{
				await Task.Delay(Timeout.Infinite, context.CancellationToken);
			}
			catch (OperationCanceledException)
			{
				cancelled.SetResult();
				throw;
			}
		}));
		ITransferItem second = _queue.Enqueue(Request("second", _ => Task.CompletedTask));
		await Eventually.TrueAsync(() => first.State == TransferState.Running, "the first transfer runs");

		_queue.Remove(first.Id);

		Assert.DoesNotContain(first, _queue.Items);
		await cancelled.Task.WaitAsync(Ct);
		await Eventually.TrueAsync(() => second.State == TransferState.Completed, "the freed slot runs the next transfer");
	}

	[Fact]
	public async Task Remove_QueuedItem_NeverRuns()
	{
		_settings.Set(new FileTransferSettings { MaxConcurrentTransfers = 1 });
		TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
		int removedRuns = 0;
		ITransferItem first = _queue.Enqueue(Request("first", context => gate.Task.WaitAsync(context.CancellationToken)));
		ITransferItem removed = _queue.Enqueue(Request("removed", context =>
		{
			_ = Interlocked.Increment(ref removedRuns);
			return Task.CompletedTask;
		}));
		ITransferItem last = _queue.Enqueue(Request("last", _ => Task.CompletedTask));
		await Eventually.TrueAsync(() => first.State == TransferState.Running, "the first transfer runs");

		_queue.Remove(removed.Id);
		gate.SetResult();

		await Eventually.TrueAsync(() => last.State == TransferState.Completed, "the transfer after the removed one runs");
		Assert.Equal(0, Volatile.Read(ref removedRuns));
		Assert.Equal([last, first], _queue.Items);
	}

	[Fact]
	public async Task Retry_OfAnItemCanceledWhileQueued_TakesItsTurnAtTheBack()
	{
		_settings.Set(new FileTransferSettings { MaxConcurrentTransfers = 1 });
		TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
		ConcurrentQueue<string> started = new();
		ITransferItem first = _queue.Enqueue(Request("first", async context =>
		{
			started.Enqueue("first");
			await gate.Task.WaitAsync(context.CancellationToken);
		}));
		ITransferItem retried = _queue.Enqueue(Request("retried", _ =>
		{
			started.Enqueue("retried");
			return Task.CompletedTask;
		}));
		ITransferItem other = _queue.Enqueue(Request("other", _ =>
		{
			started.Enqueue("other");
			return Task.CompletedTask;
		}));
		await Eventually.TrueAsync(() => first.State == TransferState.Running, "the first transfer runs");

		_queue.Cancel(retried.Id);
		_queue.Retry(retried.Id);
		gate.SetResult();

		await Eventually.TrueAsync(() => retried.State == TransferState.Completed && other.State == TransferState.Completed, "both run");
		Assert.Equal(["first", "other", "retried"], started);
	}

	[Fact]
	public async Task Dispose_CancelsRunningAndQueuedItems()
	{
		_settings.Set(new FileTransferSettings { MaxConcurrentTransfers = 1 });
		ITransferItem running = _queue.Enqueue(Request("running", context => Task.Delay(Timeout.Infinite, context.CancellationToken)));
		ITransferItem queued = _queue.Enqueue(Request("queued", _ => Task.CompletedTask));
		await Eventually.TrueAsync(() => running.State == TransferState.Running, "the first transfer runs");

		_queue.Dispose();

		Assert.Equal(TransferState.Canceled, queued.State);
		await Eventually.TrueAsync(() => running.State == TransferState.Canceled, "the running transfer stops");
		_ = Assert.Throws<ObjectDisposedException>(() => _queue.Enqueue(Request("late", _ => Task.CompletedTask)));
	}

	public void Dispose() => _queue.Dispose();

	private static TransferRequest Request(string name, Func<TransferContext, Task> execute) => new()
	{
		Name = name,
		Direction = TransferDirection.Upload,
		Source = name,
		Destination = "/srv/upload/" + name,
		ExecuteAsync = execute,
	};

	private static async Task ReportAllAsync(Channel<long> reports, TransferContext context)
	{
		await foreach (long bytes in reports.Reader.ReadAllAsync(context.CancellationToken))
		{
			context.Progress.Report(bytes);
		}
	}
}
