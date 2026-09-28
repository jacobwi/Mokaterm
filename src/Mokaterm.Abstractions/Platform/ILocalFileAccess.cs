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

	/// <summary>Returns the picked files, or an empty list when cancelled.</summary>
	ValueTask<IReadOnlyList<LocalFileItem>> PickFilesAsync(bool multiple = true, CancellationToken cancellationToken = default);

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
}
