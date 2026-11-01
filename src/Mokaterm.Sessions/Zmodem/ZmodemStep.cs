namespace Mokaterm.Sessions.Zmodem;

/// <summary>
/// What one step of a ZMODEM session produced: bytes to send to the other side, bytes that belong in the terminal, and
/// the events the caller must act on (write a file chunk, offer a file, feed the sender, notice the end). The caller
/// drains it after every engine call; a fresh step is filled each time.
/// </summary>
internal sealed class ZmodemStep
{
	/// <summary>Bytes for the channel, in order.</summary>
	public readonly List<byte> Send = [];

	/// <summary>Bytes that are terminal output rather than protocol, handed back while the session only watches.</summary>
	public readonly List<byte> Passthrough = [];

	/// <summary>Chunks of the file being received, in order, to write to the local file.</summary>
	public readonly List<byte[]> FileData = [];

	/// <summary>An offered file waits for the user's answer. Set at most once per file.</summary>
	public ZmodemFileInfo? Offer;

	/// <summary>A file started moving; its bytes count from zero now.</summary>
	public bool FileStarted;

	/// <summary>The file being received ended cleanly: its last chunk is in <see cref="FileData"/> and the local copy can be closed.</summary>
	public bool FileFinished;

	/// <summary>The sender wants up to <see cref="DataWanted"/> bytes of the current file, read from <see cref="DataPosition"/>.</summary>
	public bool DataWanted;

	public int DataWantedBytes;

	/// <summary>Where the file bytes the sender wants start: 0 to begin, or the position a retransmit resumes at.</summary>
	public long DataPosition;

	/// <summary>The session ended; <see cref="FailedReason"/> says why when it did not end well.</summary>
	public bool Done;

	/// <summary>User-worded reason of a failed or aborted session; null when it ended well.</summary>
	public string? FailedReason;

	/// <summary>The peer canceled or the session collapsed; the bytes already sent stay sent.</summary>
	public bool Aborted;

	public bool IsEmpty => Send.Count == 0 && Passthrough.Count == 0 && FileData.Count == 0
		&& Offer is null && !FileStarted && !FileFinished && !DataWanted && !Done;
}
