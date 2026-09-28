namespace Mokaterm.DevHost.Demo.FileSystem;

/// <summary>A file as a read sees it: its size and the bytes the demo stores, which may be only the beginning.</summary>
internal sealed record DemoFileData(long Size, byte[] Content, DateTimeOffset Modified)
{
	/// <summary>True when every byte is stored, so <see cref="Content"/> is the whole file.</summary>
	public bool IsComplete => Content.Length >= Size;
}
