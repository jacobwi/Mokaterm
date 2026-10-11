using System.Text;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>How a call becomes the line the login shell runs, and the script and arguments sudo runs instead.</summary>
public sealed class DockerCommandLineTests
{
	[Theory]
	[InlineData("web", "web")]
	[InlineData("ghcr.io/acme/app:2.4@sha256:ab", "ghcr.io/acme/app:2.4@sha256:ab")]
	[InlineData("--format", "--format")]
	[InlineData("a,b+c=d%e", "a,b+c=d%e")]
	[InlineData("", "''")]
	[InlineData("two words", "'two words'")]
	[InlineData("it's", "'it'\\''s'")]
	[InlineData("$HOME", "'$HOME'")]
	[InlineData("{{json .}}", "'{{json .}}'")]
	[InlineData("=cmd", "'=cmd'")]
	[InlineData("%1", "'%1'")]
	[InlineData("a;b", "'a;b'")]
	[InlineData("`id`", "'`id`'")]
	[InlineData("tab\there", "'tab\there'")]
	public void Word_LeavesPlainWordsBare_AndQuotesTheRest(string value, string expected) =>
		Assert.Equal(expected, DockerCommandLine.Word(value));

	[Fact]
	public void Build_AWorkingDirectory_ChangesIntoItFirst()
	{
		DockerCall call = new() { Program = DockerProgram.ComposePlugin, Arguments = ["ps"], WorkingDirectory = "/srv/my shop" };

		Assert.Equal("cd -- '/srv/my shop' && docker compose ps", DockerCommandLine.Build(call));
	}

	[Fact]
	public void Build_AScript_RunsUnderShWithItsArgumentsApart()
	{
		DockerCall call = DockerCall.Script("for f do echo \"$f\"; done", ["/srv/a b", "-x"]);

		Assert.Equal("sh -c 'for f do echo \"$f\"; done' sh '/srv/a b' -x", DockerCommandLine.Build(call));
	}

	[Fact]
	public void ForScript_ACommand_GoesInAsArguments_NeverAsScriptText()
	{
		DockerCall call = DockerCommands.Stop("web");

		(string script, IReadOnlyList<string> arguments) = DockerCommandLine.ForScript(call);

		Assert.Equal(DockerCommandLine.ElevatedScript, script);
		Assert.Equal(["docker", "stop", "web"], arguments);
	}

	[Fact]
	public void ForScript_AWorkingDirectory_IsTheFirstArgument()
	{
		DockerCall call = new() { Program = DockerProgram.ComposeStandalone, Arguments = ["up", "--detach"], WorkingDirectory = "/srv/shop" };

		(string script, IReadOnlyList<string> arguments) = DockerCommandLine.ForScript(call);

		Assert.Equal(DockerCommandLine.ElevatedScriptInDirectory, script);
		Assert.Equal(["/srv/shop", "docker-compose", "up", "--detach"], arguments);
	}

	[Fact]
	public void ForScript_AScriptOfOurOwn_IsTheScript()
	{
		DockerCall call = DockerCall.Script("printf %s \"$1\"", ["a b"]);

		(string script, IReadOnlyList<string> arguments) = DockerCommandLine.ForScript(call);

		Assert.Equal("printf %s \"$1\"", script);
		Assert.Equal(["a b"], arguments);
	}

	[Fact(Timeout = 30_000)]
	public async Task TheLine_ReachesTheProgramWordForWord()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX shell on this machine.");
			return;
		}

		string[] awkward = ["two words", "it's", "$HOME", "`id`", "a;b", "=x", "--flag", "{{json .}}", ""];
		DockerCall call = new() { Program = DockerProgram.Docker, Arguments = awkward };

		// docker is a function here that prints each word it gets on a line of its own, between brackets.
		string script = "docker() { for w do printf '[%s]\\n' \"$w\"; done; }\n" + DockerCommandLine.Build(call) + "\n";
		(int exit, byte[] output, string error) = await RunAsync(shell, script, [], cancellationToken);

		Assert.True(exit == 0, error);
		Assert.Equal(awkward.Select(word => $"[{word}]"), Encoding.UTF8.GetString(output).TrimEnd('\n').Split('\n'));
	}

	[Fact(Timeout = 30_000)]
	public async Task TheElevatedScript_RunsTheArgumentsAsTheCommand_InTheFolder()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		if (PosixShellLocator.Find() is not { } shell)
		{
			Assert.Skip("No POSIX shell on this machine.");
			return;
		}

		string name = $"mokaterm docker {Guid.NewGuid():N}";
		string folder = Path.Combine(Path.GetTempPath(), name);
		Directory.CreateDirectory(folder);
		try
		{
			DockerCall call = new() { Program = DockerProgram.Docker, Arguments = ["a b"], WorkingDirectory = await PosixPathAsync(shell, folder, cancellationToken) };
			(string script, IReadOnlyList<string> arguments) = DockerCommandLine.ForScript(call);

			// "docker" is swapped for a command that prints where it runs and what it got.
			Assert.Equal("docker", arguments[1]);
			string[] swapped = [arguments[0], "sh", "-c", "pwd; printf '[%s]' \"$@\"", "sh", .. arguments.Skip(2)];
			(int exit, byte[] output, string error) = await RunAsync(shell, script, swapped, cancellationToken);

			Assert.True(exit == 0, error);
			string[] lines = Encoding.UTF8.GetString(output).TrimEnd('\n').Split('\n');
			Assert.EndsWith(name, lines[0], StringComparison.Ordinal);
			Assert.Equal("[a b]", lines[1]);
		}
		finally
		{
			Directory.Delete(folder, recursive: true);
		}
	}

	private static async Task<string> PosixPathAsync(string shell, string windowsOrPosixPath, CancellationToken cancellationToken)
	{
		if (OperatingSystem.IsWindows())
		{
			(_, byte[] output, _) = await RunAsync(shell, "cygpath -u \"$1\"", [windowsOrPosixPath], cancellationToken);
			return Encoding.UTF8.GetString(output).Trim();
		}

		return windowsOrPosixPath;
	}

	private static Task<(int ExitCode, byte[] Output, string Error)> RunAsync(string shell, string script, IEnumerable<string> arguments, CancellationToken cancellationToken) =>
		PosixShellLocator.RunAsync(shell, script, arguments).WaitAsync(cancellationToken);
}
