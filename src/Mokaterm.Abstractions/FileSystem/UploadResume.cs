namespace Mokaterm.Abstractions.FileSystem;

/// <summary>
/// What lets a failed upload be continued instead of started over. The caller keeps one instance per upload and passes it
/// in <see cref="UploadOptions.Resume"/> on every attempt; the file system records the partial file it writes, keeps that
/// file when the attempt fails, and on the next attempt carries on from what it holds. Attempts run one at a time.
/// </summary>
/// <remarks>
/// A file system that cannot keep a partial upload ignores this and starts over, which is what an upload did before.
/// Progress always counts what the current attempt sent; <see cref="NotifyResumed"/> says where that started.
/// </remarks>
public sealed class UploadResume
{
	private readonly Action<long>? _resumed;

	/// <param name="resumed">Told the offset whenever an attempt continues a partial file instead of starting over.</param>
	public UploadResume(Action<long>? resumed = null) => _resumed = resumed;

	/// <summary>The partial file a failed attempt left, or null when there is nothing to continue.</summary>
	public string? PartialPath { get; private set; }

	/// <summary>
	/// True when <see cref="PartialPath"/> is a file of our own beside the target, which nobody else wants once the upload is
	/// abandoned. False when the partial is the target itself (FTP writes in place), which may be all that is left of a file
	/// the upload was replacing, so it is never deleted on the caller's behalf.
	/// </summary>
	public bool IsTemporary { get; private set; }

	/// <summary>Records the file the current attempt writes into. Called by the file system before the first byte.</summary>
	public void Record(string partialPath, bool isTemporary)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(partialPath);
		PartialPath = partialPath;
		IsTemporary = isTemporary;
	}

	/// <summary>Forgets the partial file: it was moved into place, removed, or must not be continued.</summary>
	public void Clear()
	{
		PartialPath = null;
		IsTemporary = false;
	}

	/// <summary>Called by the file system when the current attempt continues from <paramref name="offset"/>.</summary>
	public void NotifyResumed(long offset)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		_resumed?.Invoke(offset);
	}
}
