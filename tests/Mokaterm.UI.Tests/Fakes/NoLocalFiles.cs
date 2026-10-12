using Mokaterm.Abstractions.Platform;

namespace Mokaterm.UI.Tests.Fakes;

/// <summary>Present so the parts of a page that need a picker show; a static render never opens one.</summary>
internal sealed class NoLocalFiles : ILocalFileAccess
{
	public bool CanPickFolders => false;

	public ValueTask<IReadOnlyList<LocalFileItem>> PickFilesAsync(bool multiple = true, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public ValueTask<IReadOnlyList<LocalFileItem>> PickFolderAsync(CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public ValueTask<bool> SaveFileAsync(string suggestedName, long? length, Func<Stream, CancellationToken, Task> writeAsync, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();
}
