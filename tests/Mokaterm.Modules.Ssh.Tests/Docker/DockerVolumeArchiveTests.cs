using System.Text;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

// Volume backup and restore: the exact calls, the names and paths they refuse, the helper image they pick, and the
// clean-up a run gets however it ended.
public sealed class DockerVolumeArchiveTests
{
	private const string Helper = "mokaterm-volume-0123456789ab";

	private static readonly string ImageHex = new('a', 64);

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public void TheBackup_IsOneScript_WithEveryValueAsAnArgument()
	{
		DockerCall call = DockerVolumeArchive.Backup("shop_pgdata", "sha256:" + ImageHex, "/srv/backups/db.tar.gz", Helper, "/srv/backups/.mokaterm-x.part", gzip: true);

		Assert.Equal(DockerProgram.Shell, call.Program);
		Assert.Equal([DockerVolumeArchive.BackupScript, "sh", Helper, "shop_pgdata", ImageHex, "/srv/backups/db.tar.gz", "/srv/backups/.mokaterm-x.part", "1", "21600"], call.Arguments);
		Assert.EndsWith($"' sh {Helper} shop_pgdata {ImageHex} /srv/backups/db.tar.gz /srv/backups/.mokaterm-x.part 1 21600", call.Display, StringComparison.Ordinal);
		Assert.Equal(TimeSpan.FromHours(6) + TimeSpan.FromMinutes(1), call.Timeout);
		Assert.True(call.RunsLong);
		Assert.Equal(DockerCall.LiveOutputBytes, call.OutputLimitBytes);
	}

	[Fact]
	public void TheRestore_IsOneScript_AndSaysWhetherItMakesTheVolume()
	{
		DockerCall existing = DockerVolumeArchive.Restore("shop_pgdata", createVolume: false, ImageHex, "/tmp/db backup.tar", Helper);
		DockerCall created = DockerVolumeArchive.Restore("shop_copy", createVolume: true, ImageHex, "/tmp/db.tar", Helper);

		Assert.EndsWith($"' sh {Helper} shop_pgdata {ImageHex} '/tmp/db backup.tar' 0 21600", existing.Display, StringComparison.Ordinal);
		Assert.Equal([DockerVolumeArchive.RestoreScript, "sh", Helper, "shop_copy", ImageHex, "/tmp/db.tar", "1", "21600"], created.Arguments);
	}

	[Fact]
	public void TheHealthWatch_TakesARunToTouchItsHelperAlone()
	{
		DockerCall call = DockerVolumeArchive.Backup("data", ImageHex, "/tmp/a.tar", Helper, "/tmp/.p", gzip: false);
		DockerContainerState helper = new() { Id = new string('b', 64), Name = Helper, State = "created" };
		DockerContainerState web = new() { Id = new string('c', 64), Name = "web", State = "running" };

		DockerChangeScope scope = DockerChangeScope.Of(call);

		Assert.True(scope.Covers(helper));
		Assert.False(scope.Covers(web));
		Assert.Equal($"docker rm --force --volumes {Helper}", DockerVolumeArchive.RemoveHelper(Helper).Display);
		Assert.True(DockerChangeScope.Of(DockerVolumeArchive.RemoveHelper(Helper)).Covers(helper));
	}

	[Theory]
	[InlineData("-v", "/tmp/a.tar")]
	[InlineData("a,b", "/tmp/a.tar")]
	[InlineData("data", "a.tar")]
	[InlineData("data", "/tmp/a\nb.tar")]
	public void ANameOrPath_DockerOrTheShellWouldMisread_IsRefused(string volume, string target) =>
		Assert.Throws<ArgumentException>(() => DockerVolumeArchive.Backup(volume, ImageHex, target, Helper, "/tmp/.p", gzip: false));

	[Fact]
	public void OnlyAnImageIdAndAHelperOfOurOwn_AreTaken()
	{
		Assert.Throws<ArgumentException>(() => DockerVolumeArchive.Backup("data", "busybox:latest", "/tmp/a.tar", Helper, "/tmp/.p", gzip: false));
		Assert.Throws<ArgumentException>(() => DockerVolumeArchive.Backup("data", ImageHex, "/tmp/a.tar", "web", "/tmp/.p", gzip: false));
		Assert.Throws<ArgumentException>(() => DockerVolumeArchive.RemoveHelper("shop-db-1"));
		Assert.Throws<ArgumentException>(() => DockerVolumeArchive.Restore("bad name", createVolume: true, ImageHex, "/tmp/a.tar", Helper));
	}

	[Fact]
	public void Helpers_AndPartialFiles_GetNamesNothingElseHas()
	{
		string first = DockerVolumeArchive.NewHelperName();
		string second = DockerVolumeArchive.NewHelperName();

		Assert.Matches("^mokaterm-volume-[0-9a-f]{12}$", first);
		Assert.NotEqual(first, second);
		Assert.Null(DockerNames.NewNameProblem(first, "container"));
		Assert.Matches("^/srv/backups/\\.mokaterm-[0-9a-f]{16}\\.part$", DockerVolumeArchive.PartialFile("/srv/backups/db.tar.gz"));
	}

	[Fact]
	public void TheHelperImage_IsTheSmallestOneWithATag()
	{
		DockerImage busybox = Image("busybox", "latest", "4.26MB", 'b');
		DockerImage postgres = Image("postgres", "16", "432MB", 'c');
		DockerImage dangling = Image("<none>", "<none>", "1MB", 'd');

		Assert.Same(busybox, DockerVolumeArchive.PickHelperImage([postgres, dangling, busybox]));
		Assert.Same(dangling, DockerVolumeArchive.PickHelperImage([dangling]));
		Assert.Null(DockerVolumeArchive.PickHelperImage([]));
	}

	[Theory]
	[InlineData("db.tar", null)]
	[InlineData("db.tar.gz", null)]
	[InlineData("db.tgz", null)]
	[InlineData("", "Name the file")]
	[InlineData("..", "no '/'")]
	[InlineData("a/b.tar", "no '/'")]
	[InlineData("db.zip", "End the name in .tar")]
	public void BackupNames_EndInTarOrTarGz(string name, string? expected)
	{
		string? problem = DockerVolumeArchive.BackupNameProblem(name);

		if (expected is null)
		{
			Assert.Null(problem);
		}
		else
		{
			Assert.Contains(expected, problem, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void Names_SayWhatTheArchiveHolds()
	{
		Assert.True(DockerVolumeArchive.IsGzipName("a.TAR.GZ"));
		Assert.False(DockerVolumeArchive.IsGzipName("a.tar"));
		Assert.True(DockerVolumeArchive.IsRestorable("a.tar.xz"));
		Assert.True(DockerVolumeArchive.IsRestorable("a.tbz2"));
		Assert.False(DockerVolumeArchive.IsRestorable("a.zip"));
		Assert.StartsWith("shop_pgdata-", DockerVolumeArchive.DefaultFileName("shop_pgdata", DateTimeOffset.UnixEpoch, gzip: true), StringComparison.Ordinal);
		Assert.EndsWith(".tar", DockerVolumeArchive.DefaultFileName("data", DateTimeOffset.UnixEpoch, gzip: false), StringComparison.Ordinal);
	}

	[Fact]
	public void TheScripts_ReadAsSh_WithNoCarriageReturnOrSingleQuote()
	{
		foreach (string script in new[] { DockerVolumeArchive.BackupScript, DockerVolumeArchive.RestoreScript })
		{
			Assert.DoesNotContain('\r', script);
			Assert.DoesNotContain('\'', script);
		}
	}

	[Fact]
	public async Task TheCleanUp_RunsAfterASuccess()
	{
		RecordingShell shell = new();
		await using DockerSessionFeature feature = Feature(shell);

		DockerLiveRun run = feature.StartLive(Plan(), feature.Client);
		await run.Completion.WaitAsync(TimeSpan.FromSeconds(15), Ct);

		await Eventually.TrueAsync(() => shell.Asked.Contains($"docker rm --force --volumes {Helper}"), "the helper is removed");
		Assert.Equal(DockerLiveState.Succeeded, run.State);
		Assert.Equal("/tmp/a.tar", run.Plan.Produces);
	}

	[Fact]
	public async Task TheCleanUp_RunsAfterAFailure()
	{
		RecordingShell shell = new() { Exit = 1 };
		await using DockerSessionFeature feature = Feature(shell);

		DockerLiveRun run = feature.StartLive(Plan(), feature.Client);
		await run.Completion.WaitAsync(TimeSpan.FromSeconds(15), Ct);

		await Eventually.TrueAsync(() => shell.Asked.Contains($"docker rm --force --volumes {Helper}"), "the helper is removed");
		Assert.Equal(DockerLiveState.Failed, run.State);
	}

	[Fact]
	public async Task TheCleanUp_RunsAfterAStop_AndTheStopSaysWhatIsLeft()
	{
		RecordingShell shell = new() { Hold = true };
		await using DockerSessionFeature feature = Feature(shell);

		DockerLiveRun run = feature.StartLive(Plan() with { StopNote = "Stopped. The partial file goes." }, feature.Client);
		await Eventually.TrueAsync(() => shell.Asked.Count == 1, "the run started");
		run.Cancel();
		await run.Completion.WaitAsync(TimeSpan.FromSeconds(15), Ct);

		await Eventually.TrueAsync(() => shell.Asked.Contains($"docker rm --force --volumes {Helper}"), "the helper is removed after the stop");
		Assert.Equal(DockerLiveState.Stopped, run.State);
		Assert.Equal("Stopped. The partial file goes.", run.Problem);
	}

	private static DockerSessionFeature Feature(RecordingShell shell) =>
		new(shell, authenticate: null, TimeProvider.System, "docker-host", "alice", files: null);

	private static DockerLivePlan Plan() => new(
		[DockerVolumeArchive.Backup("data", ImageHex, "/tmp/a.tar", Helper, "/tmp/.mokaterm-x.part", gzip: false)],
		"Back up data",
		"Copies it.",
		"Back up",
		DockerLivePlan.VolumesOwner,
		ErrorIsProgress: false)
	{
		Cleanup = [DockerVolumeArchive.RemoveHelper(Helper)],
		Produces = "/tmp/a.tar",
	};

	private static DockerImage Image(string repository, string tag, string size, char id) =>
		new() { Id = "sha256:" + new string(id, 64), Repository = repository, Tag = tag, Size = size };

	// The script's call holds until cancelled or exits with Exit; every other call (the clean-up) answers at once.
	private sealed class RecordingShell : IDockerShell
	{
		private readonly Lock _lock = new();
		private readonly List<string> _asked = [];

		public bool Hold { get; init; }

		public int Exit { get; init; }

		public bool IsElevated => false;

		public IReadOnlyList<string> Asked
		{
			get
			{
				lock (_lock)
				{
					return [.. _asked];
				}
			}
		}

		public async Task<DockerOutput> RunAsync(DockerCall call, CancellationToken cancellationToken)
		{
			string line = DockerCommandLine.Build(call);
			lock (_lock)
			{
				_asked.Add(call.Program == DockerProgram.Shell ? "script" : line);
			}

			if (call.Program != DockerProgram.Shell)
			{
				return new DockerOutput { Command = line, ExitCode = 0 };
			}

			call.OnOutput?.Invoke(Mokaterm.Abstractions.Commands.CommandStream.Output, Encoding.UTF8.GetBytes("Copying data\n"));
			if (Hold)
			{
				await Task.Delay(Timeout.Infinite, cancellationToken);
			}

			return new DockerOutput { Command = line, ExitCode = Exit, Error = Exit == 0 ? "" : "Error response from daemon: Could not find the file" };
		}
	}
}
