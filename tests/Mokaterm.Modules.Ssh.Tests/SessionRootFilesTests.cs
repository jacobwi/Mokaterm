using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.FileSystem;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>
/// The one root file system a session's tools share: opened once, kept for the session, never disposed by a tool that
/// only borrows it.
/// </summary>
public sealed class SessionRootFilesTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task OpensRootOnce_ForEveryToolThatAsks()
	{
		CountingFiles files = new();
		await using SessionRootFiles root = new(files);
		await using DockerHostFiles docker = new(root);

		IRemoteFileSystem first = await root.OpenAsync(Ct);
		IRemoteFileSystem fromDocker = await docker.OpenAsync(asRoot: true, Ct);

		Assert.Same(first, fromDocker);
		Assert.Equal(1, files.RootOpens);
	}

	[Fact]
	public async Task OpensTogether_ShareTheOneThatOpens()
	{
		CountingFiles files = new() { RootDelay = TimeSpan.FromMilliseconds(50) };
		await using SessionRootFiles root = new(files);

		IRemoteFileSystem[] opened = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => root.OpenAsync(Ct))).WaitAsync(TimeSpan.FromSeconds(10), Ct);

		Assert.All(opened, view => Assert.Same(opened[0], view));
		Assert.Equal(1, files.RootOpens);
	}

	[Fact]
	public async Task ARefusedRoot_IsAskedForAgain()
	{
		CountingFiles files = new() { RefuseRoot = true };
		await using SessionRootFiles root = new(files);

		await Assert.ThrowsAsync<RemoteFileSystemException>(() => root.OpenAsync(Ct));
		files.RefuseRoot = false;
		await root.OpenAsync(Ct);

		Assert.Equal(2, files.RootOpens);
	}

	[Fact]
	public async Task ABorrowingTool_LeavesTheRootViewToTheSession()
	{
		CountingFiles files = new();
		SessionRootFiles root = new(files);
		DockerHostFiles docker = new(root);
		Probe view = (Probe)await docker.OpenAsync(asRoot: true, Ct);

		await docker.DisposeAsync();
		Assert.False(view.Disposed);
		Assert.Same(view, await root.OpenAsync(Ct));

		await root.DisposeAsync();
		Assert.True(view.Disposed);
		await Assert.ThrowsAsync<RemoteFileSystemException>(() => root.OpenAsync(Ct));
	}

	[Fact]
	public async Task AToolWithFilesOfItsOwn_DisposesItsRootView()
	{
		CountingFiles files = new();
		DockerHostFiles docker = new(files);
		Probe view = (Probe)await docker.OpenAsync(asRoot: true, Ct);

		await docker.DisposeAsync();

		Assert.True(view.Disposed);
	}

	private sealed class CountingFiles : IFileSystemFeature
	{
		private int _rootOpens;

		public bool RefuseRoot { get; set; }

		public TimeSpan RootDelay { get; init; }

		public int RootOpens => Volatile.Read(ref _rootOpens);

		public bool SupportsElevation => true;

		public ValueTask<IRemoteFileSystem> OpenAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IRemoteFileSystem>(new Probe());

		public async ValueTask<IRemoteFileSystem> OpenElevatedAsync(CancellationToken cancellationToken = default)
		{
			Interlocked.Increment(ref _rootOpens);
			await Task.Delay(RootDelay, cancellationToken);
			return RefuseRoot
				? throw new RemoteFileSystemException(RemoteFileErrorKind.ElevationFailed, "sudo refused.")
				: new Probe();
		}
	}

	/// <summary>Only what these tests touch: whether it was disposed.</summary>
	private sealed class Probe : IRemoteFileSystem
	{
		public bool Disposed { get; private set; }

		public RemoteFileSystemFeatures Features => RemoteFileSystemFeatures.None;

		public string UserName => "root";

		public bool IsElevated => true;

		public ValueTask DisposeAsync()
		{
			Disposed = true;
			return ValueTask.CompletedTask;
		}

		public ValueTask<string> GetHomeDirectoryAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask<string> ResolvePathAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask<IReadOnlyList<RemoteFileEntry>> ListAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask<RemoteFileEntry?> StatAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask DeleteAsync(string path, bool recursive, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask RenameAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask SetPermissionsAsync(string path, UnixFileMode permissions, bool recursive, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask SetOwnerAsync(string path, string owner, string? group, bool recursive, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask UploadAsync(string path, Stream source, UploadOptions options, IProgress<long>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask DownloadAsync(string path, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
	}
}
