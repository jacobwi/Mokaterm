using System.Text;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>The two scripts the manager runs on the host itself: which files exist, and where compose files are.</summary>
public sealed class DockerHostScriptsTests
{
	[Fact]
	public void FileStates_PassesEveryPathAsAnArgument()
	{
		DockerCall call = DockerHostScripts.FileStates(["/srv/shop/compose.yaml", "/srv/my shop/.env", "/srv/-odd"]);

		Assert.Equal(DockerProgram.Shell, call.Program);
		Assert.Equal(
			"sh -c 'for f do if [ -f \"$f\" ]; then printf 1; else printf 0; fi; done' sh /srv/shop/compose.yaml '/srv/my shop/.env' /srv/-odd",
			call.Display);
	}

	[Theory]
	[InlineData("relative/compose.yaml")]
	[InlineData("/srv/line\nbreak")]
	[InlineData("")]
	public void FileStates_APathThatIsNotAbsoluteAndClean_IsRefused(string path) =>
		Assert.ThrowsAny<ArgumentException>(() => DockerHostScripts.FileStates([path]));

	[Theory]
	[InlineData("101", 3, new[] { true, false, true })]
	[InlineData("1\n", 1, new[] { true })]
	[InlineData("1", 3, new[] { true, false, false })]
	[InlineData("", 2, new[] { false, false })]
	public void ParseFileStates_OneAnswerPerPath_AndMissingMeansMissing(string output, int count, bool[] expected) =>
		Assert.Equal(expected, DockerHostScripts.ParseFileStates(output, count));

	[Fact]
	public void FindComposeFiles_SearchesHomeAndTheUsualRoots_ToADepth()
	{
		DockerCall call = DockerHostScripts.FindComposeFiles();
		(string script, IReadOnlyList<string> arguments) = DockerCommandLine.ForScript(call);

		Assert.Equal(["4", "20", "/opt", "/srv", "/var/www", "/docker", "/data"], arguments);
		Assert.Contains("for root in \"$HOME\" \"$@\"", script, StringComparison.Ordinal);
		Assert.Contains("-maxdepth \"$depth\"", script, StringComparison.Ordinal);
		Assert.Contains("-name compose.yaml -o -name compose.yml -o -name docker-compose.yaml -o -name docker-compose.yml", script, StringComparison.Ordinal);
		Assert.DoesNotContain("\r", script, StringComparison.Ordinal);
		Assert.Equal(TimeSpan.FromSeconds((20 * 6) + 10), call.Timeout);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(9)]
	public void FindComposeFiles_ADepthOutOfRange_IsRefused(int depth) =>
		Assert.Throws<ArgumentOutOfRangeException>(() => DockerHostScripts.FindComposeFiles(depth: depth));

	[Fact]
	public void ParseFoundFiles_KeepsComposeNamesOnly_EachOnce_InOrder()
	{
		const string Output = "/srv/b/compose.yaml\r\n/srv/a/docker-compose.yml\n/srv/a/docker-compose.yml\n/srv/c/compose.override.yaml\nrelative/compose.yaml\n/srv/d/notes.txt\n\n";

		Assert.Equal(["/srv/a/docker-compose.yml", "/srv/b/compose.yaml"], DockerHostScripts.ParseFoundFiles(Output));
	}

	[Fact(Timeout = 60_000)]
	public async Task TheScripts_RunUnderARealShell()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX shell on this machine.");
			return;
		}

		string root = Path.Combine(Path.GetTempPath(), $"mokaterm-find-{Guid.NewGuid():N}");
		string project = Path.Combine(root, "my shop");
		string deep = Path.Combine(root, "a", "b", "c", "d", "e");
		string modules = Path.Combine(root, "web", "node_modules", "pkg");
		foreach (string folder in new[] { project, deep, modules })
		{
			Directory.CreateDirectory(folder);
		}

		await File.WriteAllTextAsync(Path.Combine(project, "compose.yaml"), "services: {}\n", TestContext.Current.CancellationToken);
		await File.WriteAllTextAsync(Path.Combine(deep, "compose.yaml"), "services: {}\n", TestContext.Current.CancellationToken);
		await File.WriteAllTextAsync(Path.Combine(modules, "docker-compose.yml"), "services: {}\n", TestContext.Current.CancellationToken);
		try
		{
			string posixRoot = await PosixPathAsync(shell, root, cancellationToken);
			(string script, IReadOnlyList<string> arguments) = DockerCommandLine.ForScript(DockerHostScripts.FindComposeFiles([posixRoot], depth: 3));

			// HOME points at an empty folder, so only the root given is searched.
			(int exit, byte[] output, string error) = await RunAsync(
				shell,
				PosixShellLocator.UnixPathPrefix + "HOME=/nonexistent-home\n" + script,
				arguments,
				cancellationToken);

			Assert.True(exit == 0, error);
			IReadOnlyList<string> found = DockerHostScripts.ParseFoundFiles(Encoding.UTF8.GetString(output));
			Assert.Equal([$"{posixRoot}/my shop/compose.yaml"], found);

			(string statesScript, IReadOnlyList<string> statesArguments) = DockerCommandLine.ForScript(
				DockerHostScripts.FileStates([found[0], $"{posixRoot}/my shop/.env", posixRoot]));
			(exit, output, error) = await RunAsync(shell, statesScript, statesArguments, cancellationToken);

			Assert.True(exit == 0, error);
			Assert.Equal([true, false, false], DockerHostScripts.ParseFileStates(Encoding.UTF8.GetString(output), 3));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	private static async Task<string> PosixPathAsync(string shell, string path, CancellationToken cancellationToken)
	{
		if (!OperatingSystem.IsWindows())
		{
			return path;
		}

		(_, byte[] output, _) = await RunAsync(shell, "cygpath -u \"$1\"", [path], cancellationToken);
		return Encoding.UTF8.GetString(output).Trim();
	}

	private static Task<(int ExitCode, byte[] Output, string Error)> RunAsync(string shell, string script, IEnumerable<string> arguments, CancellationToken cancellationToken) =>
		PosixShellLocator.RunAsync(shell, script, arguments).WaitAsync(cancellationToken);
}
