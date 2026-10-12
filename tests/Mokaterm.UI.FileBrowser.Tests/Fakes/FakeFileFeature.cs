using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.FileBrowser.Tests.Fakes;

/// <summary>Hands out whichever file systems the test put in place, and counts what it opened.</summary>
internal sealed class FakeFileFeature(IRemoteFileSystem user) : IFileSystemFeature
{
	public IRemoteFileSystem User { get; set; } = user;

	public IRemoteFileSystem? Root { get; set; }

	public int Opens { get; private set; }

	public int ElevatedOpens { get; private set; }

	public bool SupportsElevation { get; init; } = true;

	public ValueTask<IRemoteFileSystem> OpenAsync(CancellationToken cancellationToken = default)
	{
		Opens++;
		return ValueTask.FromResult(User);
	}

	public ValueTask<IRemoteFileSystem> OpenElevatedAsync(CancellationToken cancellationToken = default)
	{
		ElevatedOpens++;
		return ValueTask.FromResult(Root ?? throw new RemoteFileSystemException(RemoteFileErrorKind.ElevationFailed, "No root view."));
	}
}
