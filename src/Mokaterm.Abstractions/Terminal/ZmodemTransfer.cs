using Mokaterm.Abstractions.Platform;

namespace Mokaterm.Abstractions.Terminal;

/// <summary>Where a session's ZMODEM transfer stands. Idle means nothing was detected yet.</summary>
public enum ZmodemPhase
{
	/// <summary>No transfer: the watcher reads the terminal output but nothing matched.</summary>
	Idle,

	/// <summary>The other side offers a file (<c>sz</c> ran there); the user can accept, skip or cancel.</summary>
	Offering,

	/// <summary>A file is being received; <see cref="ITerminalZmodem.Progress"/> reports how far.</summary>
	Receiving,

	/// <summary>The other side waits to receive files (<c>rz</c> ran there); the user picks what to send.</summary>
	ChoosingFiles,

	/// <summary>Files are being sent; <see cref="ITerminalZmodem.Progress"/> reports how far.</summary>
	Sending,

	/// <summary>The transfer ended; <see cref="ITerminalZmodem.Outcome"/> and <see cref="ITerminalZmodem.Result"/> say how.</summary>
	Done,
}

/// <summary>How a finished ZMODEM transfer ended.</summary>
public enum ZmodemOutcome
{
	Completed,
	Canceled,
	Failed,
}

/// <summary>One file the other side wants to send, as its ZFILE frame described it.</summary>
public sealed record ZmodemFileOffer
{
	public required string FileName { get; init; }

	/// <summary>The sender's stated size. The wire carries it in one 32-bit field, so it can be missing or stale.</summary>
	public long? Length { get; init; }

	/// <summary>The sender's stated modification time; null when the sender did not send one.</summary>
	public DateTimeOffset? Modified { get; init; }
}

/// <summary>Progress of the file moving now.</summary>
/// <param name="Receiving">True while this machine downloads; false while it uploads.</param>
public sealed record ZmodemProgress(string FileName, bool Receiving, long TransferredBytes, long? TotalBytes, double BytesPerSecond);

/// <summary>
/// The ZMODEM (<c>rz</c>/<c>sz</c>) transfer of one terminal session. While a transfer runs, the terminal's stream routes
/// protocol bytes to the transfer instead of the view, so the terminal keeps working as a terminal around it. Raised on
/// any thread; methods may be called from any thread.
/// </summary>
public interface ITerminalZmodem
{
	/// <summary>Raised after <see cref="Phase"/>, <see cref="Offer"/>, <see cref="Progress"/> or <see cref="Result"/> changed.</summary>
	event Action? Changed;

	ZmodemPhase Phase { get; }

	/// <summary>The offered file while <see cref="Phase"/> is <see cref="ZmodemPhase.Offering"/>; null otherwise.</summary>
	ZmodemFileOffer? Offer { get; }

	/// <summary>Progress while a file moves; null otherwise.</summary>
	ZmodemProgress? Progress { get; }

	/// <summary>How the transfer ended while <see cref="Phase"/> is <see cref="ZmodemPhase.Done"/>.</summary>
	ZmodemOutcome Outcome { get; }

	/// <summary>A sentence about the ended transfer, written for the user; null while one runs.</summary>
	string? Result { get; }

	/// <summary>
	/// Accepts the offered file and receives it into the transfer download directory (or a save dialog when the settings
	/// ask for one each time). Returns false when the save was declined, which skips the file.
	/// </summary>
	Task<bool> AcceptAsync(CancellationToken cancellationToken = default);

	/// <summary>Declines the offered file; the other side moves to its next one.</summary>
	void Skip();

	/// <summary>Aborts the transfer with ZMODEM's cancel sequence and hands the terminal back.</summary>
	void Cancel();

	/// <summary>Sends the picked files to the waiting <c>rz</c>. An empty list cancels instead.</summary>
	void SendFiles(IReadOnlyList<LocalFileItem> files);
}
