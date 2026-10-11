namespace Mokaterm.Abstractions.Transfers;

public enum TransferDirection
{
	Upload,
	Download,

	/// <summary>Both ends are remote, for example a copy between two sessions.</summary>
	Remote,
}

public enum TransferState
{
	Queued,
	Running,
	Completed,
	Failed,
	Canceled,
}

/// <summary>
/// Whether a transfer's copy was compared with the server's checksum. A mismatch is not one of these: it fails the
/// transfer, with the reason as its error.
/// </summary>
public enum TransferVerification
{
	/// <summary>Verification is off, or the transfer has not got that far.</summary>
	NotChecked,

	/// <summary>The bytes have moved and the server is computing its checksum.</summary>
	Checking,

	/// <summary>Both sides computed the same checksum.</summary>
	Verified,

	/// <summary>The server could not compute a checksum, so the copy is unchecked.</summary>
	Unavailable,
}

/// <summary>
/// Runs and tracks file transfers for one UI scope with a concurrency limit. The queue does not know how to
/// move bytes: each request carries its own work delegate, so uploads, downloads and remote copies share one
/// progress, cancel and retry pipeline.
/// </summary>
public interface ITransferQueue
{
	/// <summary>
	/// Newest first. Items are live: <see cref="Enqueue"/> returns the one object that stands for a transfer until it
	/// leaves the queue, and its properties change in place.
	/// </summary>
	IReadOnlyList<ITransferItem> Items { get; }

	/// <summary>Items still to finish: queued plus running.</summary>
	int ActiveCount { get; }

	/// <summary>Raised on state changes and, throttled, on progress. Handlers may run on any thread.</summary>
	event Action? Changed;

	ITransferItem Enqueue(TransferRequest request);

	void Cancel(Guid id);

	/// <summary>Runs a failed or canceled item's work again from the start.</summary>
	void Retry(Guid id);

	void Remove(Guid id);

	/// <summary>Removes completed, failed and canceled items.</summary>
	void ClearFinished();
}

public interface ITransferItem
{
	Guid Id { get; }

	string Name { get; }

	TransferDirection Direction { get; }

	/// <summary>Human-readable source, such as a local file name or <c>abc@host:/etc/hosts</c>.</summary>
	string Source { get; }

	string Destination { get; }

	/// <summary>Label to group items by, usually the session title.</summary>
	string? Group { get; }

	/// <summary>True when the transfer runs as root.</summary>
	bool Elevated { get; }

	TransferState State { get; }

	long? TotalBytes { get; }

	long TransferredBytes { get; }

	/// <summary>Smoothed rate while running, 0 otherwise.</summary>
	double BytesPerSecond { get; }

	string? Error { get; }

	DateTimeOffset QueuedAt { get; }

	DateTimeOffset? StartedAt { get; }

	DateTimeOffset? FinishedAt { get; }

	TransferVerification Verification => TransferVerification.NotChecked;

	/// <summary>
	/// Where the running or last attempt picked up, when it continued what a failed one left instead of starting over.
	/// Null for an attempt that started from the first byte.
	/// </summary>
	long? ResumedFrom => null;
}

public sealed record TransferRequest
{
	public required string Name { get; init; }

	public required TransferDirection Direction { get; init; }

	public required string Source { get; init; }

	public required string Destination { get; init; }

	public long? TotalBytes { get; init; }

	public string? Group { get; init; }

	public bool Elevated { get; init; }

	/// <summary>
	/// Moves the bytes. Report cumulative progress and honour cancellation through the context. Throwing
	/// <see cref="OperationCanceledException"/> ends the item as Canceled (use it when the user dismisses a save dialog);
	/// any other exception ends it as Failed with the exception message.
	/// </summary>
	public required Func<TransferContext, Task> ExecuteAsync { get; init; }

	/// <summary>
	/// Called once when the item leaves the queue without having completed (removed, cleared, or the queue closing), so the
	/// work can delete what a failed attempt kept for resuming. Failures are logged and go no further.
	/// </summary>
	public Func<Task>? DiscardAsync { get; init; }
}

/// <summary>Handed to a transfer's work delegate.</summary>
public sealed class TransferContext
{
	private readonly Action<long> _setTotalBytes;
	private readonly Action<TransferVerification> _reportVerification;
	private readonly Action<long> _reportResumed;

	public TransferContext(IProgress<long> progress, Action<long> setTotalBytes, CancellationToken cancellationToken)
		: this(progress, setTotalBytes, static _ => { }, static _ => { }, cancellationToken)
	{
	}

	public TransferContext(
		IProgress<long> progress,
		Action<long> setTotalBytes,
		Action<TransferVerification> reportVerification,
		Action<long> reportResumed,
		CancellationToken cancellationToken)
	{
		Progress = progress;
		_setTotalBytes = setTotalBytes;
		_reportVerification = reportVerification;
		_reportResumed = reportResumed;
		CancellationToken = cancellationToken;
	}

	/// <summary>Cumulative bytes transferred, counting what an earlier attempt already moved when this one resumed.</summary>
	public IProgress<long> Progress { get; }

	public CancellationToken CancellationToken { get; }

	/// <summary>Sets the size once it is known, when the request could not know it upfront.</summary>
	public void SetTotalBytes(long totalBytes) => _setTotalBytes(totalBytes);

	public void ReportVerification(TransferVerification verification) => _reportVerification(verification);

	/// <summary>Says this attempt continues from <paramref name="offset"/> instead of the first byte.</summary>
	public void ReportResumed(long offset) => _reportResumed(offset);
}
