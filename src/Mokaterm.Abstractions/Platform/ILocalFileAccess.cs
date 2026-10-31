namespace Mokaterm.Abstractions.Platform;

/// <summary>
/// A file or folder on the user's machine, picked or dropped. On the web host the bytes stream from the
/// browser; on desktop they come from disk.
/// </summary>
public sealed record LocalFileItem
{
	public required string Name { get; init; }

	/// <summary>Path relative to the pick or drop root with '/' separators, for example <c>site/css/app.css</c>.</summary>
	public required string RelativePath { get; init; }

	public bool IsDirectory { get; init; }

	public long? Length { get; init; }

	public DateTimeOffset? LastModified { get; init; }

	/// <summary>Opens the file for reading. Null for directories.</summary>
	public Func<CancellationToken, ValueTask<Stream>>? OpenReadAsync { get; init; }
}

/// <summary>Picking and saving local files. Each host implements it with its own dialogs.</summary>
public interface ILocalFileAccess
{
	bool CanPickFolders { get; }

	/// <summary>
	/// Returns the picked files, or an empty list when cancelled. <paramref name="extensions"/>, when given, is what
	/// the picker offers, such as <c>[".mokaplugin"]</c>; a host that cannot filter still returns what the user picked.
	/// </summary>
	ValueTask<IReadOnlyList<LocalFileItem>> PickFilesAsync(bool multiple = true, IReadOnlyList<string>? extensions = null, CancellationToken cancellationToken = default);

	/// <summary>Returns the picked folder followed by everything inside it, directories before their contents.</summary>
	ValueTask<IReadOnlyList<LocalFileItem>> PickFolderAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Lets go of the files a pick returned, once nothing will open them again: pass the list <see cref="PickFilesAsync"/>
	/// or <see cref="PickFolderAsync"/> returned. The web host keeps picked files in the page until then, and opening one
	/// afterwards fails. Hosts that read files from disk have nothing to release.
	/// </summary>
	ValueTask ReleaseAsync(IReadOnlyList<LocalFileItem> picked) => ValueTask.CompletedTask;

	/// <summary>
	/// Saves a file. <paramref name="writeAsync"/> receives the destination stream; on the web host it runs when
	/// the browser starts the download. Returns false when the user cancelled.
	/// </summary>
	ValueTask<bool> SaveFileAsync(string suggestedName, long? length, Func<Stream, CancellationToken, Task> writeAsync, CancellationToken cancellationToken = default);

	/// <summary>
	/// True when <see cref="SaveFileAsync(LocalSave, Func{Stream, long, CancellationToken, Task}, CancellationToken)"/> keeps
	/// a failed download's partial file and continues it on the next attempt. Only a host that writes to its own disk can:
	/// a browser download cannot be reopened.
	/// </summary>
	bool CanResumeSaves => false;

	/// <summary>
	/// Saves through <paramref name="save"/>, which remembers where the first attempt wrote. <paramref name="writeAsync"/>
	/// gets the destination and the bytes it already holds; a host that resumes positions the stream after them and makes
	/// it readable and seekable, so a caller can read the part already there. The default starts over every time.
	/// </summary>
	ValueTask<bool> SaveFileAsync(LocalSave save, Func<Stream, long, CancellationToken, Task> writeAsync, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(save);
		ArgumentNullException.ThrowIfNull(writeAsync);
		return SaveFileAsync(save.SuggestedName, save.Length, (stream, token) => writeAsync(stream, 0, token), cancellationToken);
	}

	/// <summary>
	/// Removes what a failed save left for resuming. Called when the download is abandoned, and before starting it over when
	/// the part already there cannot be trusted. Nothing to do when the save never wrote or already finished.
	/// </summary>
	ValueTask DiscardAsync(LocalSave save) => ValueTask.CompletedTask;
}
