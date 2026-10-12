using Mokaterm.Abstractions.Transfers;

namespace Mokaterm.UI.FileBrowser.Tests.Fakes;

/// <summary>A live item, the way the queue hands them out: its state changes in place.</summary>
internal sealed class FakeTransferItem(string name) : ITransferItem
{
	public Guid Id { get; } = Guid.NewGuid();

	public string Name => name;

	public TransferDirection Direction => TransferDirection.Upload;

	public string Source => "";

	public string Destination => "";

	public string? Group => null;

	public bool Elevated => false;

	public TransferState State { get; set; } = TransferState.Queued;

	public long? TotalBytes => null;

	public long TransferredBytes => 0;

	public double BytesPerSecond => 0;

	public string? Error => null;

	public DateTimeOffset QueuedAt => DateTimeOffset.UnixEpoch;

	public DateTimeOffset? StartedAt => null;

	public DateTimeOffset? FinishedAt => null;
}
