using System.Text;
using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Storage;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Tests.Admin;

/// <summary>What's using space: du and the large file search, their output from GNU and BusyBox, and the paths they take.</summary>
public sealed class SpaceTests
{
	private static readonly TimeSpan ShellTimeout = TimeSpan.FromSeconds(60);

	[Fact]
	public void Measure_GnuDu_TotalFoldersAndComplaints()
	{
		const string Output = """
			@@mt:du
			du: cannot read directory '/var/lib/docker': Permission denied
			4	/var/lib/docker
			729088	/var/lib/postgresql
			123904	/var/lib/apt
			du: cannot read directory '/var/lib/private': Permission denied
			8	/var/lib/private
			6098944	/var/lib
			@@mt:exit 1
			""";

		SpaceReport report = SpaceCommands.ParseMeasure(Output, "/var/lib/", truncated: false);

		Assert.Equal("/var/lib", report.Path);
		Assert.Equal(6098944, report.TotalKb);
		Assert.Equal(["postgresql", "apt", "private", "docker"], report.Entries.Select(entry => entry.Name));
		Assert.Equal(2, report.Unreadable);
		Assert.Equal("du: cannot read directory '/var/lib/docker': Permission denied", report.FirstProblem);
		Assert.Equal(6098944 - 729088 - 123904 - 8 - 4, report.FilesHereKb);
		Assert.False(report.StoppedEarly);
	}

	[Fact]
	public void Measure_BusyBoxDu_UnderTheRoot_TidiesItsDoubleSlashes()
	{
		const string Output = """
			@@mt:du
			du: can't open '/root': Permission denied
			1200	//bin
			56000	//usr
			4	//my folder
			88000	/
			@@mt:exit 1
			""";

		SpaceReport report = SpaceCommands.ParseMeasure(Output, "/", truncated: false);

		Assert.Equal(88000, report.TotalKb);
		Assert.Equal(["/usr", "/bin", "/my folder"], report.Entries.Select(entry => entry.Path));
		Assert.Equal("my folder", report.Entries[2].Name);
		Assert.Equal(1, report.Unreadable);
	}

	[Fact]
	public void Measure_StoppedByTimeout_KeepsWhatItFinished()
	{
		SpaceReport report = SpaceCommands.ParseMeasure("@@mt:du\n1000\t/srv/a\n@@mt:exit 124\n", "/srv", truncated: false);

		Assert.True(report.StoppedEarly);
		Assert.Null(report.TotalKb);
		Assert.Null(report.FilesHereKb);
		Assert.Equal("/srv/a", Assert.Single(report.Entries).Path);
	}

	[Fact]
	public void LargeFiles_SizesInBytes_CappedWhenHeadCutTheList()
	{
		StringBuilder output = new("@@mt:files\n");
		for (int index = 0; index < SpaceCommands.MaxLargeFiles; index++)
		{
			output.Append(System.Globalization.CultureInfo.InvariantCulture, $"{(index + 1) * 1048576L}\t/srv/data/file {index}.bin\n");
		}

		SpaceReport capped = SpaceCommands.ParseLargeFiles(output.ToString(), "/srv/data", truncated: false);
		SpaceReport whole = SpaceCommands.ParseLargeFiles("@@mt:files\n209715200\t/var/log/big.log\n1025\t/var/log/tiny\n@@mt:exit 0\n", "/var/log", truncated: false);
		SpaceReport stopped = SpaceCommands.ParseLargeFiles("@@mt:files\n@@mt:exit 124\n", "/", truncated: false);

		Assert.True(capped.Capped);
		Assert.Equal(SpaceCommands.MaxLargeFiles * 1024, capped.Entries[0].Kb);
		Assert.False(whole.Capped);
		Assert.Equal([204800L, 2L], whole.Entries.Select(entry => entry.Kb));
		Assert.True(stopped.StoppedEarly);
		Assert.Empty(stopped.Entries);
	}

	[Theory]
	[InlineData("/", null, "/")]
	[InlineData("/var", "/", "var")]
	[InlineData("/var/log/", "/var", "log")]
	[InlineData("//srv//data", "/srv", "data")]
	public void Paths_ParentAndName(string path, string? parent, string name)
	{
		Assert.Equal(parent, SpaceCommands.Parent(path));
		Assert.Equal(name, SpaceCommands.NameOf(path));
	}

	[Fact]
	public void Trail_FromTheRootDown()
	{
		Assert.Equal([("/", "/"), ("var", "/var"), ("log", "/var/log")], SpaceCommands.Trail("/var/log"));
		Assert.Equal([("/", "/")], SpaceCommands.Trail("/"));
	}

	[Theory]
	[InlineData("/var/log", true)]
	[InlineData("/mnt/my share/it's here", true)]
	[InlineData("var/log", false)]
	[InlineData("/var/../etc", false)]
	[InlineData("/var/./log", false)]
	[InlineData("/var/log\nrm", false)]
	[InlineData("", false)]
	public void FolderPaths(string path, bool allowed) => Assert.Equal(allowed, AdminNames.CheckFolderPath(path) is null);

	[Theory]
	[InlineData("sda", true)]
	[InlineData("nvme0n1", true)]
	[InlineData("cciss!c0d0", true)]
	[InlineData("-d", false)]
	[InlineData("../sda", false)]
	[InlineData("sda b", false)]
	public void BlockDevices(string name, bool allowed) => Assert.Equal(allowed, AdminNames.CheckBlockDevice(name) is null);

	[Fact]
	public void Scripts_QuoteThePath_AndStayOnOneFileSystem()
	{
		AdminScript measure = SpaceCommands.Measure("/mnt/it's", asRoot: true);
		AdminScript large = SpaceCommands.LargeFiles("/srv", 102400, asRoot: false);

		Assert.Contains("mt_path='/mnt/it'\\''s'", measure.Body, StringComparison.Ordinal);
		Assert.Contains("du -x -k -d 1 -- \"$mt_path\"", measure.Body, StringComparison.Ordinal);
		Assert.True(measure.AsRoot);
		Assert.Contains("-xdev -type f -size +102400k", large.Body, StringComparison.Ordinal);
		Assert.Throws<ArgumentException>(() => SpaceCommands.Measure("/var/..", asRoot: false));
	}

	[Fact]
	public async Task Measure_RunsInALocalShell()
	{
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
			return;
		}

		await using TempFolder folder = new();
		Directory.CreateDirectory(Path.Combine(folder.Path, "big"));
		Directory.CreateDirectory(Path.Combine(folder.Path, "small one"));
		await File.WriteAllBytesAsync(Path.Combine(folder.Path, "big", "data.bin"), new byte[300 * 1024], TestContext.Current.CancellationToken);
		await File.WriteAllBytesAsync(Path.Combine(folder.Path, "small one", "note.txt"), new byte[10], TestContext.Current.CancellationToken);
		string unixPath = await UnixPathAsync(shell, folder.Path);

		SpaceReport usage = SpaceCommands.ParseMeasure(await RunAsync(shell, SpaceCommands.Measure(unixPath, asRoot: false)), unixPath, truncated: false);
		SpaceReport large = SpaceCommands.ParseLargeFiles(await RunAsync(shell, SpaceCommands.LargeFiles(unixPath, 100, asRoot: false)), unixPath, truncated: false);

		Assert.NotNull(usage.TotalKb);
		Assert.Equal(["big", "small one"], usage.Entries.Select(entry => entry.Name));
		Assert.True(usage.Entries[0].Kb >= 300);
		SpaceEntry file = Assert.Single(large.Entries);
		Assert.Equal(unixPath + "/big/data.bin", file.Path);
		Assert.Equal(300, file.Kb);
		Assert.False(large.Capped);
	}

	private static async Task<string> RunAsync(string shell, AdminScript script)
	{
		(int exitCode, byte[] output, string error) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + AdminScriptText.Compose(script), []).WaitAsync(ShellTimeout, TestContext.Current.CancellationToken);
		Assert.True(exitCode == 0, error);
		Assert.True(RemoteOutput.TryGetPayload(output, out ReadOnlyMemory<byte> payload));
		return Encoding.UTF8.GetString(payload.Span);
	}

	/// <summary>The folder as the local sh names it: the same on Linux, <c>/c/Users/...</c> under Git for Windows.</summary>
	private static async Task<string> UnixPathAsync(string shell, string path)
	{
		if (!OperatingSystem.IsWindows())
		{
			return path;
		}

		(_, byte[] output, _) = await PosixShellLocator.RunAsync(shell, PosixShellLocator.UnixPathPrefix + "cygpath -u \"$1\"", [path]).WaitAsync(ShellTimeout, TestContext.Current.CancellationToken);
		return Encoding.UTF8.GetString(output).Trim();
	}
}
