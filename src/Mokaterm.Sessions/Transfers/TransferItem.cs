using Mokaterm.Abstractions.Transfers;

namespace Mokaterm.Sessions.Transfers;

/// <summary>One transfer in the queue. <see cref="TransferQueue"/> changes it under its lock; readers see plain values.</summary>
internal sealed class TransferItem : ITransferItem, IDisposable
{
	/// <summary>Progress closer together than this is folded into the next rate sample; tiny gaps make wild rates.</summary>
	private static readonly TimeSpan RateSampleInterval = TimeSpan.FromMilliseconds(200);

	/// <summary>Seconds for the smoothed rate to move about two thirds of the way to a new steady rate.</summary>
	private const double RateTimeConstantSeconds = 2;

	private long _sampleBytes;
	private long _sampleTimestamp;
	private bool _hasRate;

	public TransferItem(TransferRequest request, DateTimeOffset queuedAt, long sequence)
	{
		Request = request;
		QueuedAt = queuedAt;
		Sequence = sequence;
		TotalBytes = request.TotalBytes;
	}

	public Guid Id { get; } = Guid.NewGuid();

	public string Name => Request.Name;

	public TransferDirection Direction => Request.Direction;

	public string Source => Request.Source;

	public string Destination => Request.Destination;

	public string? Group => Request.Group;

	public bool Elevated => Request.Elevated;

	public TransferState State { get; private set; }

	public long? TotalBytes { get; private set; }

	public long TransferredBytes { get; private set; }

	public double BytesPerSecond { get; private set; }

	public string? Error { get; private set; }

	public DateTimeOffset QueuedAt { get; private set; }

	public DateTimeOffset? StartedAt { get; private set; }

	public DateTimeOffset? FinishedAt { get; private set; }

	public TransferVerification Verification { get; private set; }

	public long? ResumedFrom { get; private set; }

	internal TransferRequest Request { get; }

	/// <summary>Position in the start order. Retrying takes a new one at the back.</summary>
	internal long Sequence { get; private set; }

	/// <summary>Counts starts, so reports from an earlier run of a retried item are ignored.</summary>
	internal int Run { get; private set; }

	internal CancellationTokenSource? Cancellation { get; private set; }

	internal bool IsFinished => State is TransferState.Completed or TransferState.Failed or TransferState.Canceled;

	/// <summary>Set when the item left the queue while its work was still stopping; the work discards once it has.</summary>
	internal bool DiscardWhenStopped { get; set; }

	/// <summary>True once <see cref="TransferRequest.DiscardAsync"/> was started, which happens at most once.</summary>
	internal bool Discarded { get; set; }

	internal CancellationToken Start(DateTimeOffset startedAt, long timestamp)
	{
		Run++;
		Cancellation = new CancellationTokenSource();
		State = TransferState.Running;
		StartedAt = startedAt;
		Verification = TransferVerification.NotChecked;
		ResumedFrom = null;
		_sampleBytes = 0;
		_sampleTimestamp = timestamp;
		_hasRate = false;
		return Cancellation.Token;
	}

	internal void RecordProgress(long transferredBytes, long timestamp, TimeProvider timeProvider)
	{
		if (transferredBytes < _sampleBytes)
		{
			// The work restarted its count, for example after reopening a stream.
			_sampleBytes = transferredBytes;
			_sampleTimestamp = timestamp;
		}

		TransferredBytes = transferredBytes;
		TimeSpan elapsed = timeProvider.GetElapsedTime(_sampleTimestamp, timestamp);
		if (elapsed < RateSampleInterval)
		{
			return;
		}

		double rate = (transferredBytes - _sampleBytes) / elapsed.TotalSeconds;
		double weight = 1 - Math.Exp(-elapsed.TotalSeconds / RateTimeConstantSeconds);
		BytesPerSecond = _hasRate ? BytesPerSecond + (weight * (rate - BytesPerSecond)) : rate;
		_hasRate = true;
		_sampleBytes = transferredBytes;
		_sampleTimestamp = timestamp;
	}

	internal void SetTotalBytes(long totalBytes) => TotalBytes = totalBytes;

	internal void SetVerification(TransferVerification verification) => Verification = verification;

	/// <summary>
	/// The attempt continues from <paramref name="offset"/>. The rate starts counting from there, or the bytes an earlier
	/// attempt moved would read as arriving in the first sample.
	/// </summary>
	internal void MarkResumed(long offset, long timestamp)
	{
		ResumedFrom = offset;
		TransferredBytes = offset;
		_sampleBytes = offset;
		_sampleTimestamp = timestamp;
		_hasRate = false;
	}

	internal void Finish(TransferState state, string? error, DateTimeOffset finishedAt)
	{
		State = state;
		Error = error;
		FinishedAt = finishedAt;
		BytesPerSecond = 0;
		Cancellation?.Dispose();
		Cancellation = null;
	}

	internal void Requeue(DateTimeOffset queuedAt, long sequence)
	{
		State = TransferState.Queued;
		Sequence = sequence;
		QueuedAt = queuedAt;
		StartedAt = null;
		FinishedAt = null;
		Error = null;
		TransferredBytes = 0;
		BytesPerSecond = 0;
		TotalBytes = Request.TotalBytes;
		Verification = TransferVerification.NotChecked;
		ResumedFrom = null;
	}

	public void Dispose()
	{
		Cancellation?.Dispose();
		Cancellation = null;
	}
}
