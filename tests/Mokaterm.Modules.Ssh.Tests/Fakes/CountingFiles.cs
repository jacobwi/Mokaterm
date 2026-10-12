using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ssh.Tests.Fakes;

/// <summary>A session's files that count how often root was asked for, the way sudo would be.</summary>
internal sealed class CountingFiles : IFileSystemFeature
{
	private int _rootOpens;

	public MemoryFiles User { get; } = new(elevated: false);

	public bool RefuseRoot { get; set; }

	public TimeSpan RootDelay { get; init; }

	public int RootOpens => Volatile.Read(ref _rootOpens);

	public bool SupportsElevation => true;

	public ValueTask<IRemoteFileSystem> OpenAsync(CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<IRemoteFileSystem>(User);

	public async ValueTask<IRemoteFileSystem> OpenElevatedAsync(CancellationToken cancellationToken = default)
	{
		Interlocked.Increment(ref _rootOpens);
		await Task.Delay(RootDelay, cancellationToken);
		return RefuseRoot
			? throw new RemoteFileSystemException(RemoteFileErrorKind.ElevationFailed, "sudo: 3 incorrect password attempts")
			: new MemoryFiles(elevated: true);
	}
}
