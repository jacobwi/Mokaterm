namespace Mokaterm.UI.FileBrowser.Transfers;

/// <summary>
/// Turns the bytes one attempt moved into the bytes the transfer has, which differ once an attempt resumed: a file system
/// counts what it sends now, the queue shows how far the whole file is. Reports go straight through, on the caller's
/// thread, because <see cref="Progress{T}"/> would post each one to a context that may be busy with the transfer itself.
/// </summary>
internal sealed class OffsetProgress(IProgress<long> inner) : IProgress<long>
{
	private long _offset;

	/// <summary>Where the current attempt started. Set before it moves its first byte.</summary>
	public long Offset
	{
		get => Volatile.Read(ref _offset);
		set => Volatile.Write(ref _offset, value);
	}

	public void Report(long value) => inner.Report(Offset + value);
}
