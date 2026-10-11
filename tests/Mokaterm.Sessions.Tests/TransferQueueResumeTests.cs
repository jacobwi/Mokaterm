using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Transfers;
using Mokaterm.Sessions.Tests.Fakes;
using Mokaterm.Sessions.Transfers;

namespace Mokaterm.Sessions.Tests;

/// <summary>What the queue keeps for verified and resumed transfers, and when it lets go of what a failed attempt kept.</summary>
public sealed class TransferQueueResumeTests : IDisposable
{
	private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 11, 8, 0, 0, TimeSpan.Zero));
	private readonly UncachedSettingsService _settings = new();
	private readonly TransferQueue _queue;

	public TransferQueueResumeTests() => _queue = new TransferQueue(_settings, _time, NullLogger<TransferQueue>.Instance);

	[Fact]
	public async Task Verification_ReportedByTheWork_IsOnTheItem_AndARetryStartsItOver()
	{
		int runs = 0;

		// The retry waits here until the reset has been looked at; a free-running retry can report Verified before then.
		TaskCompletionSource retryMayRun = new(TaskCreationOptions.RunContinuationsAsynchronously);
		ITransferItem item = _queue.Enqueue(Request("a", async context =>
		{
			if (++runs > 1)
			{
				await retryMayRun.Task.WaitAsync(context.CancellationToken);
			}

			context.ReportVerification(TransferVerification.Checking);
			context.ReportVerification(TransferVerification.Verified);
			if (runs == 1)
			{
				throw new IOException("The connection dropped.");
			}
		}));
		await Eventually.TrueAsync(() => item.State == TransferState.Failed, "the first attempt fails");
		Assert.Equal(TransferVerification.Verified, item.Verification);

		_queue.Retry(item.Id);

		Assert.Equal(TransferVerification.NotChecked, item.Verification);
		retryMayRun.SetResult();
		await Eventually.TrueAsync(() => item.State == TransferState.Completed, "the retry completes");
		Assert.Equal(TransferVerification.Verified, item.Verification);
	}

	// The bytes an earlier attempt moved must not read as arriving in the first sample of this one.
	[Fact]
	public async Task Resumed_ShowsWhereTheAttemptStarted_AndTheRateCountsFromThere()
	{
		TaskCompletionSource resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource advanced = new(TaskCreationOptions.RunContinuationsAsynchronously);
		ITransferItem item = _queue.Enqueue(Request("a", async context =>
		{
			context.ReportResumed(400);
			resumed.SetResult();
			await advanced.Task.WaitAsync(context.CancellationToken);
			context.Progress.Report(700);
			await Task.Delay(Timeout.Infinite, context.CancellationToken);
		}));
		await resumed.Task.WaitAsync(TestContext.Current.CancellationToken);

		_time.Advance(TimeSpan.FromSeconds(1));
		advanced.SetResult();

		await Eventually.TrueAsync(() => item.TransferredBytes == 700, "the progress arrives");
		Assert.Equal(400, item.ResumedFrom);
		Assert.Equal(300, item.BytesPerSecond, 3);
		_queue.Cancel(item.Id);
	}

	[Fact]
	public async Task Remove_AFailedItem_DiscardsWhatItKept()
	{
		int discards = 0;
		ITransferItem item = _queue.Enqueue(Request("a", _ => Task.FromException(new IOException("Dropped.")), () => Interlocked.Increment(ref discards)));
		await Eventually.TrueAsync(() => item.State == TransferState.Failed, "the transfer fails");

		_queue.Remove(item.Id);

		await Eventually.TrueAsync(() => Volatile.Read(ref discards) == 1, "the leftovers are discarded");
	}

	[Fact]
	public async Task Retry_KeepsWhatTheFailedAttemptLeft()
	{
		int discards = 0;
		int runs = 0;
		ITransferItem item = _queue.Enqueue(Request(
			"a",
			_ => ++runs == 1 ? Task.FromException(new IOException("Dropped.")) : Task.CompletedTask,
			() => Interlocked.Increment(ref discards)));
		await Eventually.TrueAsync(() => item.State == TransferState.Failed, "the first attempt fails");

		_queue.Retry(item.Id);
		await Eventually.TrueAsync(() => item.State == TransferState.Completed, "the retry completes");
		_queue.ClearFinished();

		await Eventually.StaysTrueAsync(() => Volatile.Read(ref discards) == 0, "a completed transfer has nothing to discard");
	}

	[Fact]
	public async Task ClearFinished_DiscardsFailedAndCanceled_ButNotCompleted()
	{
		List<string> discarded = [];
		Lock gate = new();
		void Record(string name)
		{
			lock (gate)
			{
				discarded.Add(name);
			}
		}

		ITransferItem failed = _queue.Enqueue(Request("failed", _ => Task.FromException(new IOException("Dropped.")), () => Record("failed")));
		ITransferItem canceled = _queue.Enqueue(Request("canceled", context => Task.Delay(Timeout.Infinite, context.CancellationToken), () => Record("canceled")));
		ITransferItem completed = _queue.Enqueue(Request("completed", _ => Task.CompletedTask, () => Record("completed")));
		await Eventually.TrueAsync(() => canceled.State == TransferState.Running, "the long transfer runs");
		_queue.Cancel(canceled.Id);
		await Eventually.TrueAsync(
			() => failed.State == TransferState.Failed && canceled.State == TransferState.Canceled && completed.State == TransferState.Completed,
			"every transfer finishes");

		_queue.ClearFinished();

		await Eventually.TrueAsync(() => { lock (gate) { return discarded.Count == 2; } }, "the two unfinished ones are discarded");
		lock (gate)
		{
			Assert.Equal(["canceled", "failed"], discarded.Order(StringComparer.Ordinal));
		}
	}

	// Deleting a partial file while the upload still writes into it would break that upload, so the discard waits.
	[Fact]
	public async Task Remove_ARunningItem_DiscardsOnlyOnceItsWorkStopped()
	{
		int stopped = 0;
		int discardedWhileRunning = -1;
		ITransferItem item = _queue.Enqueue(Request(
			"a",
			async context =>
			{
				try
				{
					await Task.Delay(Timeout.Infinite, context.CancellationToken);
				}
				finally
				{
					Volatile.Write(ref stopped, 1);
				}
			},
			() => Volatile.Write(ref discardedWhileRunning, Volatile.Read(ref stopped) == 0 ? 1 : 0)));
		await Eventually.TrueAsync(() => item.State == TransferState.Running, "the transfer runs");

		_queue.Remove(item.Id);

		await Eventually.TrueAsync(() => Volatile.Read(ref discardedWhileRunning) >= 0, "the leftovers are discarded");
		Assert.Equal(0, Volatile.Read(ref discardedWhileRunning));
	}

	[Fact]
	public async Task Dispose_DiscardsWhatUnfinishedItemsKept()
	{
		int discards = 0;
		ITransferItem failed = _queue.Enqueue(Request("failed", _ => Task.FromException(new IOException("Dropped.")), () => Interlocked.Increment(ref discards)));
		ITransferItem running = _queue.Enqueue(Request("running", context => Task.Delay(Timeout.Infinite, context.CancellationToken), () => Interlocked.Increment(ref discards)));
		await Eventually.TrueAsync(() => failed.State == TransferState.Failed && running.State == TransferState.Running, "one fails and one runs");

		_queue.Dispose();

		await Eventually.TrueAsync(() => Volatile.Read(ref discards) == 2, "both are discarded");
	}

	[Fact]
	public async Task Discard_ThatThrows_DoesNotReachTheCaller()
	{
		ITransferItem item = _queue.Enqueue(Request("a", _ => Task.FromException(new IOException("Dropped.")), () => throw new IOException("The server went away.")));
		await Eventually.TrueAsync(() => item.State == TransferState.Failed, "the transfer fails");

		_queue.Remove(item.Id);

		Assert.Empty(_queue.Items);
	}

	public void Dispose() => _queue.Dispose();

	private static TransferRequest Request(string name, Func<TransferContext, Task> execute, Action? discard = null) => new()
	{
		Name = name,
		Direction = TransferDirection.Upload,
		Source = name,
		Destination = "/srv/upload/" + name,
		ExecuteAsync = execute,
		DiscardAsync = discard is null ? null : () =>
		{
			discard();
			return Task.CompletedTask;
		},
	};
}
