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
}

/// <summary>Handed to a transfer's work delegate.</summary>
public sealed class TransferContext
{
	private readonly Action<long> _setTotalBytes;

	public TransferContext(IProgress<long> progress, Action<long> setTotalBytes, CancellationToken cancellationToken)
	{
		Progress = progress;
		_setTotalBytes = setTotalBytes;
		CancellationToken = cancellationToken;
	}

	/// <summary>Cumulative bytes transferred.</summary>
	public IProgress<long> Progress { get; }

	public CancellationToken CancellationToken { get; }

	/// <summary>Sets the size once it is known, when the request could not know it upfront.</summary>
	public void SetTotalBytes(long totalBytes) => _setTotalBytes(totalBytes);
}
