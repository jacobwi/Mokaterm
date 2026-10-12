using System.Text;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Tests.Fakes;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>
/// The docker host's files as the manager reads them: one root view for the whole session, opened when first needed and
/// let go with the Docker feature, and files read for showing through the shared text rules.
/// </summary>
public sealed class DockerHostFilesTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task RootIsOpenedOnce_AndEveryRootReadReusesIt()
	{
		CountingFiles feature = new();
		await using DockerHostFiles files = new(feature);

		IRemoteFileSystem first = await files.OpenAsync(asRoot: true, Ct);
		await files.StatAsync("/var/lib/docker", asRoot: true, Ct);
		await files.ListAsync("/var/lib/docker", asRoot: true, Ct);
		IRemoteFileSystem again = await files.OpenAsync(asRoot: true, Ct);

		Assert.Same(first, again);
		Assert.Equal(1, feature.RootOpens);
	}

	[Fact]
	public async Task TheLoginsFiles_NeverOpenRoot()
	{
		CountingFiles feature = new();
		await using DockerHostFiles files = new(feature);

		await files.StatAsync("/srv/shop/.env", asRoot: false, Ct);

		Assert.Equal(0, feature.RootOpens);
	}

	[Fact]
	public async Task RootOpenedTogether_IsStillOpenedOnce()
	{
		CountingFiles feature = new() { RootDelay = TimeSpan.FromMilliseconds(50) };
		await using DockerHostFiles files = new(feature);

		IRemoteFileSystem[] opened = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => files.OpenAsync(asRoot: true, Ct)))
			.WaitAsync(TimeSpan.FromSeconds(5), Ct);

		Assert.All(opened, fileSystem => Assert.Same(opened[0], fileSystem));
		Assert.Equal(1, feature.RootOpens);
	}

	[Fact]
	public async Task ARefusedRoot_IsNotKept_SoTheNextTryAsksAgain()
	{
		CountingFiles feature = new() { RefuseRoot = true };
		await using DockerHostFiles files = new(feature);

		await Assert.ThrowsAsync<RemoteFileSystemException>(() => files.OpenAsync(asRoot: true, Ct));
		feature.RefuseRoot = false;
		await files.OpenAsync(asRoot: true, Ct);

		Assert.Equal(2, feature.RootOpens);
	}

	[Fact]
	public async Task TheRootView_GoesWithTheFeature()
	{
		CountingFiles feature = new();
		DockerSessionFeature docker = DockerSessionFeature.ForCommands(new NoCommands(), TimeProvider.System, "web01", "deploy", feature);
		DockerHostFiles files = Assert.IsType<DockerHostFiles>(docker.HostFiles);
		MemoryFiles root = (MemoryFiles)await files.OpenAsync(asRoot: true, Ct);

		await docker.DisposeAsync();

		Assert.True(root.Disposed);
		Assert.False(feature.User.Disposed);
		await Assert.ThrowsAsync<RemoteFileSystemException>(() => files.OpenAsync(asRoot: false, Ct));
	}

	[Fact]
	public void ASessionWithoutFiles_HasNone()
	{
		DockerSessionFeature docker = DockerSessionFeature.ForCommands(new NoCommands(), TimeProvider.System, "web01", "deploy");

		Assert.Null(docker.HostFiles);
	}

	[Fact]
	public async Task ReadText_ShowsTheStartOfALongFile_MarkedCut()
	{
		CountingFiles feature = new();
		feature.User.Files["/var/lib/docker/volumes/logs/_data/app.log"] = Encoding.UTF8.GetBytes(new string('x', DockerHostFiles.MaxViewBytes + 10));
		await using DockerHostFiles files = new(feature);

		RemoteTextFile file = await files.ReadTextAsync("/var/lib/docker/volumes/logs/_data/app.log", asRoot: false, Ct);

		Assert.True(file.IsCut);
		Assert.Equal(DockerHostFiles.MaxViewBytes, file.Text.Length);
	}

	[Fact]
	public async Task ReadText_KeepsFormFeedsAsText()
	{
		CountingFiles feature = new();
		feature.User.Files["/srv/shop/.env"] = Encoding.UTF8.GetBytes("A=1\fB\r\nC=2\r\n");
		await using DockerHostFiles files = new(feature);

		RemoteTextFile file = await files.ReadTextAsync("/srv/shop/.env", asRoot: false, Ct);

		Assert.Equal("A=1\fB\nC=2\n", file.Text);
		Assert.False(file.IsCut);
	}

	[Fact]
	public async Task ReadText_RefusesWhatIsNotText()
	{
		CountingFiles feature = new();
		feature.User.Files["/var/lib/docker/volumes/grafana/_data/grafana.db"] = [0x53, 0x51, 0x00, 0x4C];
		await using DockerHostFiles files = new(feature);

		RemoteFileSystemException refused = await Assert.ThrowsAsync<RemoteFileSystemException>(
			() => files.ReadTextAsync("/var/lib/docker/volumes/grafana/_data/grafana.db", asRoot: false, Ct));

		Assert.Contains("not a text file", refused.Message, StringComparison.Ordinal);
	}
}
