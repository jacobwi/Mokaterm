using Mokaterm.Abstractions.Platform;

namespace Mokaterm.UI.FileBrowser.Tests.Fakes;

/// <summary>A host with no local files: nothing under test should pick or save one.</summary>
internal sealed class NoLocalFiles : ILocalFileAccess
{
	public bool CanPickFolders => false;

	public ValueTask<IReadOnlyList<LocalFileItem>> PickFilesAsync(bool multiple = true, IReadOnlyList<string>? extensions = null, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public ValueTask<IReadOnlyList<LocalFileItem>> PickFolderAsync(CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public ValueTask<bool> SaveFileAsync(string suggestedName, long? length, Func<Stream, CancellationToken, Task> writeAsync, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();
}
