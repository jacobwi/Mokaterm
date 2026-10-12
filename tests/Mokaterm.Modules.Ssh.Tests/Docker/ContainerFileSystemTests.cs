using System.Text;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Shell;
using Mokaterm.Modules.Ssh.Tests.Fakes;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>
/// A container's files through the Docker manager's client, against a container that is this machine's own POSIX
/// shell: every script really runs, its output is parsed back, and writes go through the docker host's files the way
/// they would on a server. Skipped where there is no sh.
/// </summary>
public sealed class ContainerFileSystemTests : IAsyncLifetime
{
	private const string Container = "web-1";
	private const string Staging = "/tmp/stage";

	private readonly string? _shell = PosixShellLocator.Find();
	private readonly CountingFiles _host = new();
	private LocalContainerShell _docker = default!;
	private string _folder = "";

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	public async ValueTask InitializeAsync()
	{
		if (_shell is null)
		{
			return;
		}

		_docker = new LocalContainerShell(_shell, Container, path => _host.User.Files.TryGetValue(path, out byte[]? bytes) ? bytes : null);
		_folder = await ShellTextAsync("d=$(mktemp -d)\nmkdir \"$d/sub dir\"\nprintf 'hello' > \"$d/a b.txt\"\nprintf 'x' > \"$d/it's.conf\"\nprintf '%s' \"$d\"");
	}

	public async ValueTask DisposeAsync()
	{
		if (_shell is not null && _folder.Length > 0)
		{
			await PosixShellLocator.RunAsync(_shell, PosixShellLocator.UnixPathPrefix + "rm -rf -- \"$1\"", [_folder]);
		}
	}

	[Fact]
	public async Task Open_AsksWhoRunsThere_AndOffersOnlyWhatAContainerCan()
	{
		ContainerFileSystem files = await OpenAsync();

		Assert.NotEqual("", files.UserName);
		Assert.StartsWith("/", await files.GetHomeDirectoryAsync(Ct), StringComparison.Ordinal);
		Assert.False(files.IsElevated);
		Assert.Null(files.ServerChecksum);
		Assert.True(files.Features.HasFlag(RemoteFileSystemFeatures.Copy));
		Assert.False(files.Features.HasFlag(RemoteFileSystemFeatures.Archives));
		Assert.False(files.Features.HasFlag(RemoteFileSystemFeatures.Elevation));
		Assert.Contains(_docker.Calls, call => call.Arguments.Contains(RemoteScripts.Identity));
	}

	[Fact]
	public async Task List_ReadsNamesWithSpacesAndQuotes_WithTheirKindsAndSizes()
	{
		ContainerFileSystem files = await OpenAsync();

		List<RemoteFileEntry> entries = [.. (await files.ListAsync(_folder, Ct)).OrderBy(entry => entry.Name, StringComparer.Ordinal)];

		Assert.Equal(["a b.txt", "it's.conf", "sub dir"], entries.Select(entry => entry.Name));
		Assert.Equal(RemoteEntryKind.File, entries[0].Kind);
		Assert.Equal(5, entries[0].Size);
		Assert.Equal(_folder + "/a b.txt", entries[0].Path);
		Assert.NotNull(entries[0].LastModified);
		Assert.NotNull(entries[0].Permissions);
		Assert.Equal(RemoteEntryKind.Directory, entries[2].Kind);
	}

	[Fact]
	public async Task Stat_DescribesAnEntry_AndIsNullForNothing()
	{
		ContainerFileSystem files = await OpenAsync();

		// Git for Windows' sh reads quotes in its own command line, so the name with a quote only travels in a listing here;
		// how such a name is quoted on the way to a server is DockerCommandsTests' business.
		RemoteFileEntry? file = await files.StatAsync(_folder + "/a b.txt", Ct);
		RemoteFileEntry? missing = await files.StatAsync(_folder + "/nope", Ct);

		Assert.Equal("a b.txt", file?.Name);
		Assert.Equal(5, file?.Size);
		Assert.Null(missing);
	}

	[Fact]
	public async Task Read_ComesInGrowingPieces_AndIsTheFileByteForByte()
	{
		string path = _folder + "/data.bin";
		await ShellTextAsync("dd if=/dev/urandom of=\"$1\" bs=1000 count=300 2>/dev/null\nprintf 'tail' >> \"$1\"", path);
		byte[] expected = await ShellBytesAsync("cat -- \"$1\"", path);
		ContainerFileSystem files = await OpenAsync();
		int before = ReadCalls();

		await using Stream stream = await files.OpenReadAsync(path, Ct);
		using MemoryStream read = new();
		await stream.CopyToAsync(read, Ct);

		Assert.Equal(300_004, expected.Length);
		Assert.Equal(expected, read.ToArray());

		// 256 KiB first, then a 1 MiB piece that comes back short, which is the end of the file.
		Assert.Equal(2, ReadCalls() - before);
	}

	[Fact]
	public async Task Download_FromAnOffsetInsideABlock_SendsExactlyTheRest()
	{
		string path = _folder + "/data.bin";
		await ShellTextAsync("dd if=/dev/urandom of=\"$1\" bs=1000 count=150 2>/dev/null", path);
		byte[] expected = await ShellBytesAsync("cat -- \"$1\"", path);
		ContainerFileSystem files = await OpenAsync();
		using MemoryStream destination = new();

		await files.DownloadAsync(path, destination, 70_000, progress: null, Ct);

		Assert.Equal(expected.AsSpan(70_000).ToArray(), destination.ToArray());
	}

	[Fact]
	public async Task Read_OfAFolderOrNothing_FailsWhenOpened()
	{
		ContainerFileSystem files = await OpenAsync();

		RemoteFileSystemException missing = await Assert.ThrowsAsync<RemoteFileSystemException>(async () => await files.OpenReadAsync(_folder + "/nope", Ct));
		RemoteFileSystemException folder = await Assert.ThrowsAsync<RemoteFileSystemException>(async () => await files.OpenReadAsync(_folder + "/sub dir", Ct));

		Assert.Equal(RemoteFileErrorKind.NotFound, missing.Kind);
		Assert.Contains("is a directory", folder.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Upload_IsStagedAsTheLoginsPrivateFile_PipedIn_AndCleanedUp()
	{
		ContainerFileSystem files = await OpenAsync();
		string path = _folder + "/new file.txt";

		await files.UploadAsync(path, new MemoryStream("hello container"u8.ToArray()), UploadOptions.Default, progress: null, Ct);

		Assert.Equal("hello container", await ShellTextAsync("cat -- \"$1\"", path));
		(string staged, UploadOptions options) = Assert.Single(_host.User.Uploads);
		Assert.StartsWith(Staging + "/.mokaterm-upload-", staged, StringComparison.Ordinal);
		Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, options.Permissions);
		Assert.Empty(_host.User.Files);
	}

	[Fact]
	public async Task Upload_OverAFile_IsRefusedUnlessAsked_ThenWritesInPlace()
	{
		ContainerFileSystem files = await OpenAsync();
		string path = _folder + "/a b.txt";

		RemoteFileSystemException refused = await Assert.ThrowsAsync<RemoteFileSystemException>(
			async () => await files.UploadAsync(path, new MemoryStream("new"u8.ToArray()), UploadOptions.Default, progress: null, Ct));
		await files.UploadAsync(path, new MemoryStream("new"u8.ToArray()), new UploadOptions { Overwrite = true }, progress: null, Ct);

		Assert.Equal(RemoteFileErrorKind.AlreadyExists, refused.Kind);
		Assert.Equal("new", await ShellTextAsync("cat -- \"$1\"", path));
		Assert.Empty(_host.User.Files);
	}

	[Fact]
	public async Task MakeMoveCopyAndDelete_RunTheSharedScripts()
	{
		ContainerFileSystem files = await OpenAsync();

		await files.CreateDirectoryAsync(_folder + "/made", Ct);
		await files.RenameAsync(_folder + "/made", _folder + "/moved", overwrite: false, Ct);
		await files.CopyAsync(_folder + "/a b.txt", _folder + "/moved/copy.txt", overwrite: false, Ct);
		RemoteFileSystemException taken = await Assert.ThrowsAsync<RemoteFileSystemException>(
			async () => await files.CreateDirectoryAsync(_folder + "/moved", Ct));
		Assert.Equal("hello", await ShellTextAsync("cat -- \"$1\"", _folder + "/moved/copy.txt"));

		await files.DeleteAsync(_folder + "/moved", recursive: true, Ct);

		Assert.Equal(RemoteFileErrorKind.AlreadyExists, taken.Kind);
		Assert.Null(await files.StatAsync(_folder + "/moved", Ct));
	}

	[Fact]
	public async Task Resolve_ExpandsTheContainersHome()
	{
		ContainerFileSystem files = await OpenAsync();
		string home = await files.GetHomeDirectoryAsync(Ct);

		Assert.Equal(await files.ResolvePathAsync(home, Ct), await files.ResolvePathAsync("~", Ct));
	}

	[Fact]
	public async Task WhatAContainerCannotDo_FailsWithoutRunningAnything()
	{
		ContainerFileSystem files = await OpenAsync();
		int calls = _docker.Calls.Count;

		RemoteFileSystemException archive = await Assert.ThrowsAsync<RemoteFileSystemException>(
			async () => await files.CompressAsync(_folder, ["a b.txt"], _folder + "/a.tar.gz", Ct));
		RemoteFileSystemException control = await Assert.ThrowsAsync<RemoteFileSystemException>(
			async () => await files.StatAsync(_folder + "/bell\u0007", Ct));

		Assert.Equal(RemoteFileErrorKind.NotSupported, archive.Kind);
		Assert.Equal(RemoteFileErrorKind.NotSupported, control.Kind);
		Assert.Equal(calls, _docker.Calls.Count);
	}

	[Fact]
	public async Task AContainerWithoutAShell_IsRefusedWithASentenceThatSaysSo()
	{
		SkipWithoutShell();
		_docker.DockerSays = new DockerOutput
		{
			Command = "docker exec",
			ExitCode = 127,
			Error = "OCI runtime exec failed: exec failed: unable to start container process: exec: \"sh\": executable file not found in $PATH: unknown\n",
		};

		RemoteFileSystemException refused = await Assert.ThrowsAsync<RemoteFileSystemException>(() => OpenAsync());

		Assert.Equal(RemoteFileErrorKind.NotSupported, refused.Kind);
		Assert.Contains("web-1 has no shell", refused.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AContainerThatIsGone_ReadsAsALostConnection()
	{
		SkipWithoutShell();
		ContainerFileSystemFeature feature = new(() => new DockerClient(_docker, "build-01"), "web-2", null, null);

		RemoteFileSystemException gone = await Assert.ThrowsAsync<RemoteFileSystemException>(async () => await feature.OpenAsync(Ct));

		Assert.Equal(RemoteFileErrorKind.ConnectionLost, gone.Kind);
		Assert.Contains("no container named web-2", gone.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Upload_WithoutTheHostsFiles_IsRefused()
	{
		SkipWithoutShell();
		ContainerFileSystem files = await ContainerFileSystem.OpenAsync(() => new DockerClient(_docker, "build-01"), Container, hostFiles: null, logger: null, Ct);

		RemoteFileSystemException refused = await Assert.ThrowsAsync<RemoteFileSystemException>(
			async () => await files.UploadAsync(_folder + "/x", Stream.Null, UploadOptions.Default, progress: null, Ct));

		Assert.Equal(RemoteFileErrorKind.NotSupported, refused.Kind);
	}

	[Fact]
	public async Task TheFeature_OpensOnce_AndOffersNoRoot()
	{
		SkipWithoutShell();
		await using ContainerFileSystemFeature feature = new(() => new DockerClient(_docker, "build-01"), Container, null, null);

		IRemoteFileSystem first = await feature.OpenAsync(Ct);
		IRemoteFileSystem again = await feature.OpenAsync(Ct);

		Assert.Same(first, again);
		Assert.False(feature.SupportsElevation);
		Assert.Single(_docker.Calls, call => call.Arguments.Contains(RemoteScripts.Identity));
		await Assert.ThrowsAsync<NotSupportedException>(async () => await feature.OpenElevatedAsync(Ct));
	}

	[Fact]
	public async Task TheSessionsDocker_HandsOutOneFeaturePerContainer_AndClosesThem()
	{
		DockerSessionFeature docker = DockerSessionFeature.ForCommands(new NoCommands(), TimeProvider.System, "build-01", "deploy", _host);

		IFileSystemFeature web = docker.ContainerFiles("web-1");
		Assert.Same(web, docker.ContainerFiles(" web-1 "));
		Assert.NotSame(web, docker.ContainerFiles("db-1"));
		Assert.Throws<ArgumentException>(() => docker.ContainerFiles("-it"));

		await docker.DisposeAsync();

		RemoteFileSystemException closed = await Assert.ThrowsAsync<RemoteFileSystemException>(async () => await web.OpenAsync(Ct));
		Assert.Equal(RemoteFileErrorKind.ConnectionLost, closed.Kind);
		Assert.Throws<ObjectDisposedException>(() => docker.ContainerFiles("web-1"));
	}

	private async Task<ContainerFileSystem> OpenAsync()
	{
		SkipWithoutShell();
		return await ContainerFileSystem.OpenAsync(
			() => new DockerClient(_docker, "build-01"),
			Container,
			new DockerHostFiles(_host, () => Staging),
			logger: null,
			Ct).WaitAsync(TimeSpan.FromSeconds(30), Ct);
	}

	private int ReadCalls() => _docker.Calls.Count(call => call.Arguments.Contains(RemoteScripts.ReadRange));

	private void SkipWithoutShell()
	{
		if (_shell is null)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
		}
	}

	private async Task<string> ShellTextAsync(string script, params string[] arguments) =>
		Encoding.UTF8.GetString(await ShellBytesAsync(script, arguments));

	private async Task<byte[]> ShellBytesAsync(string script, params string[] arguments)
	{
		SkipWithoutShell();
		(int exitCode, byte[] output, string error) = await PosixShellLocator.RunAsync(_shell!, PosixShellLocator.UnixPathPrefix + script, arguments);
		Assert.True(exitCode == 0, error);
		return output;
	}
}
