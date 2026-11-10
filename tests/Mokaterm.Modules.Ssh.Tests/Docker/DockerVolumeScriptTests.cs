using System.IO.Compression;
using System.Text;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

// The backup and restore scripts themselves, run by this machine's sh against a docker that is a script of its own:
// the archive goes between docker and the file and never through the script's stdout, the partial file becomes the
// backup only on success, and the helper container is removed whatever happened.
public sealed class DockerVolumeScriptTests : IDisposable
{
	private const string Helper = "mokaterm-volume-0123456789ab";
	private const string Image = "aaaaaaaaaaaa";

	private const string FakeDocker = """
		#!/bin/sh
		echo "docker $*" >> "$FAKE_LOG"
		case "$1" in
		create)
			if [ -n "$FAKE_CREATE_FAILS" ]; then echo "Unable to find image" >&2; exit 125; fi
			echo 0123456789ab
			;;
		cp)
			if [ "$2" = "--archive" ]; then
				cat > "$FAKE_RECEIVED"
				[ -z "$FAKE_CP_FAILS" ] || { echo "Error response from daemon: Error processing tar file" >&2; exit 1; }
			elif [ -n "$FAKE_CP_FAILS" ]; then
				echo "Error response from daemon: Could not find the file /mokaterm-volume in container" >&2
				exit 1
			elif [ -n "$FAKE_CP_HANGS" ]; then
				exec sleep 30
			else
				printf "pretend tar bytes"
			fi
			;;
		volume)
			if [ "$2" = inspect ] && [ -z "$FAKE_VOLUME_EXISTS" ]; then exit 1; fi
			;;
		esac
		exit 0
		""";

	private readonly string _folder = Directory.CreateTempSubdirectory("mokaterm-volume-script-").FullName;

	public void Dispose() => Directory.Delete(_folder, recursive: true);

	[Fact]
	public async Task ABackup_WritesTheArchiveThroughAPartialFile_AndRemovesTheHelper()
	{
		(int exit, string output, _) = await BackupAsync("db.tar", gzip: false);

		Assert.Equal(0, exit);
		Assert.Equal("pretend tar bytes", File.ReadAllText(Local("db.tar")));
		Assert.Equal(["db.tar", "docker", "log"], Listed());
		Assert.DoesNotContain("pretend tar bytes", output, StringComparison.Ordinal);
		Assert.Contains($"Wrote {Posix("db.tar")}", output, StringComparison.Ordinal);
		string[] log = Log();
		Assert.Equal($"docker create --name {Helper} --label com.mokaterm.helper=volume-backup --network none --mount type=volume,source=shop_pgdata,target=/mokaterm-volume,readonly,volume-nocopy {Image} /mokaterm-never-runs", log[0]);
		Assert.Equal($"docker cp {Helper}:/mokaterm-volume/. -", log[1]);
		Assert.Equal($"docker rm --force --volumes {Helper}", log[^1]);
	}

	[Fact]
	public async Task AGzipBackup_IsTheTarCompressed()
	{
		(int exit, _, _) = await BackupAsync("db.tar.gz", gzip: true);

		Assert.Equal(0, exit);
		await using FileStream file = File.OpenRead(Local("db.tar.gz"));
		await using GZipStream unzipped = new(file, CompressionMode.Decompress);
		using StreamReader reader = new(unzipped);
		Assert.Equal("pretend tar bytes", await reader.ReadToEndAsync(Ct));
	}

	[Fact]
	public async Task AFailedCopy_LeavesNoFile_AndStillRemovesTheHelper()
	{
		(int exit, _, string error) = await BackupAsync("db.tar.gz", gzip: true, "FAKE_CP_FAILS=1");

		Assert.Equal(1, exit);
		Assert.Contains("Could not find the file", error, StringComparison.Ordinal);
		Assert.Equal(["docker", "log"], Listed());
		Assert.Equal($"docker rm --force --volumes {Helper}", Log()[^1]);
	}

	[Fact]
	public async Task AHelperThatCannotBeMade_EndsTheBackup_AndIsRemovedAnyway()
	{
		(int exit, _, _) = await BackupAsync("db.tar", gzip: false, "FAKE_CREATE_FAILS=1");

		Assert.Equal(125, exit);
		Assert.Equal(["docker", "log"], Listed());
		Assert.Equal(2, Log().Length);
		Assert.Equal($"docker rm --force --volumes {Helper}", Log()[^1]);
	}

	[Fact]
	public async Task ABackup_NeverReplacesAFile()
	{
		await File.WriteAllTextAsync(Local("db.tar"), "keep me", Ct);

		(int exit, _, string error) = await BackupAsync("db.tar", gzip: false);

		Assert.Equal(3, exit);
		Assert.Contains("exists already", error, StringComparison.Ordinal);
		Assert.Equal("keep me", await File.ReadAllTextAsync(Local("db.tar"), Ct));
		Assert.DoesNotContain(Log(), line => line.StartsWith("docker create", StringComparison.Ordinal));
	}

	[Fact]
	public async Task ABackupPastItsTime_IsStopped_AndCleansUp()
	{
		(int exit, _, string error) = await BackupAsync("db.tar", gzip: false, "FAKE_CP_HANGS=1", limit: "10");

		Assert.Equal(124, exit);
		Assert.Contains("ran past the 0 minutes", error, StringComparison.Ordinal);
		Assert.Equal(["docker", "log"], Listed());
		Assert.Equal($"docker rm --force --volumes {Helper}", Log()[^1]);
	}

	[Fact]
	public async Task ARestore_FeedsTheArchiveToDockerCp_AndRemovesTheHelper()
	{
		await File.WriteAllTextAsync(Local("db.tar"), "archive bytes", Ct);

		(int exit, string output, _) = await RestoreAsync("db.tar", create: false);

		Assert.Equal(0, exit);
		Assert.Equal("archive bytes", await File.ReadAllTextAsync(Local("received"), Ct));
		Assert.Contains("Restored", output, StringComparison.Ordinal);
		string[] log = Log();
		Assert.Equal($"docker create --name {Helper} --label com.mokaterm.helper=volume-restore --network none --mount type=volume,source=shop_pgdata,target=/mokaterm-volume,volume-nocopy {Image} /mokaterm-never-runs", log[0]);
		Assert.Equal($"docker cp --archive - {Helper}:/mokaterm-volume", log[1]);
		Assert.Equal($"docker rm --force --volumes {Helper}", log[^1]);
	}

	[Fact]
	public async Task ARestoreIntoANewVolume_RefusesOneThatExists()
	{
		await File.WriteAllTextAsync(Local("db.tar"), "archive bytes", Ct);

		(int exit, _, string error) = await RestoreAsync("db.tar", create: true, "FAKE_VOLUME_EXISTS=1");

		Assert.Equal(3, exit);
		Assert.Contains("exists already", error, StringComparison.Ordinal);
		Assert.DoesNotContain(Log(), line => line.StartsWith("docker volume create", StringComparison.Ordinal) || line.StartsWith("docker volume rm", StringComparison.Ordinal));
	}

	[Fact]
	public async Task AFailedRestoreIntoANewVolume_RemovesTheVolumeAgain()
	{
		await File.WriteAllTextAsync(Local("db.tar"), "archive bytes", Ct);

		(int exit, _, _) = await RestoreAsync("db.tar", create: true, "FAKE_CP_FAILS=1");

		Assert.Equal(1, exit);
		string[] log = Log();
		Assert.Equal("docker volume create shop_pgdata", log[1]);
		Assert.Equal($"docker rm --force --volumes {Helper}", log[^2]);
		Assert.Equal("docker volume rm shop_pgdata", log[^1]);
	}

	[Fact]
	public async Task ARestoreOfAFileThatIsNotThere_SaysSo()
	{
		(int exit, _, string error) = await RestoreAsync("missing.tar", create: false);

		Assert.Equal(2, exit);
		Assert.Contains("is not a file this account can read", error, StringComparison.Ordinal);
	}

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	// Git for Windows' sh reads C: drives as /c and splits PATH on colons, so every path it sees is written its way.
	private static string ToPosix(string path) =>
		path.Length > 2 && path[1] == ':' ? "/" + char.ToLowerInvariant(path[0]) + path[2..].Replace('\\', '/') : path;

	private string Local(string name) => Path.Combine(_folder, name);

	private string Posix(string name) => ToPosix(Local(name));

	private string[] Listed() => [.. Directory.GetFiles(_folder).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

	private string[] Log() => File.Exists(Local("log")) ? File.ReadAllLines(Local("log")) : [];

	private Task<(int, string, string)> BackupAsync(string name, bool gzip, string? environment = null, string limit = "60") =>
		RunAsync(DockerVolumeArchive.BackupScript, [Helper, "shop_pgdata", Image, Posix(name), Posix(".mokaterm-0123456789abcdef.part"), gzip ? "1" : "0", limit], environment);

	private Task<(int, string, string)> RestoreAsync(string name, bool create, string? environment = null) =>
		RunAsync(DockerVolumeArchive.RestoreScript, [Helper, "shop_pgdata", Image, Posix(name), create ? "1" : "0", "60"], environment);

	private async Task<(int, string, string)> RunAsync(string script, IReadOnlyList<string> arguments, string? environment)
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return default;
		}

		await File.WriteAllTextAsync(Local("docker"), FakeDocker.ReplaceLineEndings("\n"), Ct);
		string prelude = $"PATH={ToPosix(_folder)}:/usr/bin:/bin:$PATH\nexport PATH\nchmod +x {ToPosix(Local("docker"))}\n"
			+ $"export FAKE_LOG={ToPosix(Local("log"))} FAKE_RECEIVED={ToPosix(Local("received"))}\n"
			+ (environment is null ? "" : $"export {environment}\n");
		(int exit, byte[] output, string error) = await PosixShellLocator.RunAsync(shell, prelude + script, arguments).WaitAsync(TimeSpan.FromSeconds(60), Ct);
		return (exit, Encoding.UTF8.GetString(output), error);
	}
}
