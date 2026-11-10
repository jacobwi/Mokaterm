using System.Formats.Tar;
using System.IO.Compression;
using Microsoft.Extensions.Time.Testing;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.DevHost.Demo.Docker;
using Mokaterm.DevHost.Demo.FileSystem;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

// The DevHost's demo docker played through the module's own client and live runs: a backup writes a real tar.gz of
// the volume into the demo tree, owned by the login, a restore unpacks it into a new volume, and a stopped backup
// leaves neither its helper container nor its partial file behind.
public sealed class DockerDemoVolumeTests
{
	private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(250);

	private static readonly DemoAccount Login = DemoAccount.ForUser("demo");

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ABackup_WritesTheVolumeAsATarGz_AndARestoreBringsItBack()
	{
		(FakeTimeProvider time, DemoFileTree tree, DockerSessionFeature feature) = Demo();
		await using (feature)
		{
			DockerImage helper = DockerVolumeArchive.PickHelperImage(await DriveAsync(time, feature.Client.ListImagesAsync(Ct)))!;
			Assert.Equal("busybox:latest", helper.Reference);

			DockerLiveRun backup = await RunAsync(time, feature, Backup("shop_pgdata", helper, "/tmp/db.tar.gz", gzip: true));

			Assert.Equal(DockerLiveState.Succeeded, backup.State);
			Assert.StartsWith("Wrote /tmp/db.tar.gz, ", backup.Lines.Snapshot()[^1].Text, StringComparison.Ordinal);
			RemoteFileEntry written = tree.Stat("/tmp/db.tar.gz", Login)!;
			Assert.Equal("demo", written.Owner);
			Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, written.Permissions);
			Assert.Equal(["./", "./PG_VERSION", "./base/", "./postgresql.auto.conf"], Entries(tree.Read("/tmp/db.tar.gz", Login).Content));
			Assert.DoesNotContain(tree.ListDirectory("/tmp", Login), entry => entry.Name.EndsWith(".part", StringComparison.Ordinal));

			DockerLiveRun restore = await RunAsync(time, feature, Restore("shop_copy", helper, "/tmp/db.tar.gz"));

			Assert.Equal(DockerLiveState.Succeeded, restore.State);
			Assert.Equal("16\n", System.Text.Encoding.UTF8.GetString(tree.Read("/var/lib/docker/volumes/shop_copy/_data/PG_VERSION", DemoAccount.Root).Content));
			Assert.Contains(await DriveAsync(time, feature.Client.ListVolumesAsync(Ct)), volume => volume.Name == "shop_copy");
			Assert.DoesNotContain(await DriveAsync(time, feature.Client.ListContainersAsync(Ct)), container => container.Name.StartsWith(DockerVolumeArchive.HelperPrefix, StringComparison.Ordinal));
		}
	}

	[Fact]
	public async Task AStoppedBackup_LeavesNoHelperAndNoPartialFile()
	{
		(FakeTimeProvider time, DemoFileTree tree, DockerSessionFeature feature) = Demo();
		await using (feature)
		{
			DockerImage helper = DockerVolumeArchive.PickHelperImage(await DriveAsync(time, feature.Client.ListImagesAsync(Ct)))!;
			DockerLiveRun run = feature.StartLive(Backup("shop_pgdata", helper, "/tmp/stopped.tar", gzip: false), feature.Client);
			await Eventually.AdvanceUntilAsync(time, Step, () => run.Lines.Snapshot().Count >= 1, "the backup started");
			Assert.Contains(await DriveAsync(time, feature.Client.ListContainersAsync(Ct)), container => container.Name.StartsWith(DockerVolumeArchive.HelperPrefix, StringComparison.Ordinal));

			run.Cancel();
			await Eventually.AdvanceUntilAsync(time, Step, () => run.Completion.IsCompleted, "the backup stopped");
			await Eventually.AdvanceUntilAsync(time, Step, () => !tree.ListDirectory("/tmp", Login).Any(entry => entry.Name.EndsWith(".part", StringComparison.Ordinal)), "the clean-up ran");

			Assert.Equal(DockerLiveState.Stopped, run.State);
			Assert.Null(tree.Stat("/tmp/stopped.tar", Login));
			Assert.DoesNotContain(await DriveAsync(time, feature.Client.ListContainersAsync(Ct)), container => container.Name.StartsWith(DockerVolumeArchive.HelperPrefix, StringComparison.Ordinal));
		}
	}

	[Fact]
	public async Task ABackupIntoAFolderTheLoginCannotWrite_FailsWithTheShellsReason()
	{
		(FakeTimeProvider time, _, DockerSessionFeature feature) = Demo();
		await using (feature)
		{
			DockerImage helper = DockerVolumeArchive.PickHelperImage(await DriveAsync(time, feature.Client.ListImagesAsync(Ct)))!;

			DockerLiveRun run = await RunAsync(time, feature, Backup("shop_pgdata", helper, "/srv/db.tar", gzip: false));

			Assert.Equal(DockerLiveState.Failed, run.State);
			Assert.StartsWith("sh: 1: cannot create /srv/.mokaterm-", run.Problem, StringComparison.Ordinal);
		}
	}

	[Fact]
	public async Task ARestoreIntoAVolumeThatExists_AsANewOne_IsRefused()
	{
		(FakeTimeProvider time, DemoFileTree tree, DockerSessionFeature feature) = Demo();
		await using (feature)
		{
			DockerImage helper = DockerVolumeArchive.PickHelperImage(await DriveAsync(time, feature.Client.ListImagesAsync(Ct)))!;
			_ = await RunAsync(time, feature, Backup("old-cache", helper, "/tmp/cache.tar", gzip: false));

			DockerLiveRun run = await RunAsync(time, feature, Restore("shop_pgdata", helper, "/tmp/cache.tar"));

			Assert.Equal(DockerLiveState.Failed, run.State);
			Assert.Equal("A volume named shop_pgdata exists already.", run.Problem);
			Assert.Equal(["./"], Entries(tree.Read("/tmp/cache.tar", Login).Content));
		}
	}

	private static DockerLivePlan Backup(string volume, DockerImage image, string target, bool gzip)
	{
		string helper = DockerVolumeArchive.NewHelperName();
		return new DockerLivePlan(
			[DockerVolumeArchive.Backup(volume, image.Id, target, helper, DockerVolumeArchive.PartialFile(target), gzip)],
			"Back up " + volume,
			"Copies it.",
			"Back up",
			DockerLivePlan.VolumesOwner,
			ErrorIsProgress: false)
		{
			Cleanup = [DockerVolumeArchive.RemoveHelper(helper)],
			Produces = target,
		};
	}

	private static DockerLivePlan Restore(string volume, DockerImage image, string archive)
	{
		string helper = DockerVolumeArchive.NewHelperName();
		return new DockerLivePlan(
			[DockerVolumeArchive.Restore(volume, createVolume: true, image.Id, archive, helper)],
			"Restore into " + volume,
			"Unpacks it.",
			"Restore",
			DockerLivePlan.VolumesOwner,
			ErrorIsProgress: false)
		{
			Cleanup = [DockerVolumeArchive.RemoveHelper(helper)],
		};
	}

	private static List<string> Entries(byte[] archive)
	{
		using MemoryStream source = new(archive);
		using Stream plain = archive is [0x1f, 0x8b, ..] ? new GZipStream(source, CompressionMode.Decompress) : source;
		using TarReader reader = new(plain);
		List<string> names = [];
		while (reader.GetNextEntry() is { } entry)
		{
			names.Add(entry.Name);
		}

		return names;
	}

	private static async Task<DockerLiveRun> RunAsync(FakeTimeProvider time, DockerSessionFeature feature, DockerLivePlan plan)
	{
		await Eventually.AdvanceUntilAsync(time, Step, () => !feature.LiveRuns.IsRunning, "the last run ended");
		DockerLiveRun run = feature.StartLive(plan, feature.Client);
		await Eventually.AdvanceUntilAsync(time, Step, () => run.Completion.IsCompleted, "the run ended");
		return run;
	}

	private static (FakeTimeProvider Time, DemoFileTree Tree, DockerSessionFeature Feature) Demo()
	{
		FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.Zero));
		DemoNode root = DemoNode.NewDirectory("", "root", "root", DemoModes.Octal("755"), time.GetUtcNow().AddDays(-100));
		root.Add(DemoNode.NewDirectory("tmp", "root", "root", DemoModes.Octal("777"), time.GetUtcNow().AddDays(-1)));
		DemoDockerFiles.Add(root, time.GetUtcNow());
		DemoFileTree tree = new(root, ["root", "demo", "999", "472"], ["root", "docker", "demo", "999", "472"], time);
		DemoDockerEngine engine = new(time, DemoDockerFiles.Reader(tree), DemoDockerFiles.ByteReader(tree), tree, Login);
		return (time, tree, DockerSessionFeature.ForCommands(new DemoDockerCommands(engine, time), time, "demo-docker", "demo"));
	}

	private static async Task<T> DriveAsync<T>(FakeTimeProvider time, Task<T> task)
	{
		await Eventually.AdvanceUntilAsync(time, Step, () => task.IsCompleted, "the demo answers", maxSteps: 100);
		return await task;
	}
}
