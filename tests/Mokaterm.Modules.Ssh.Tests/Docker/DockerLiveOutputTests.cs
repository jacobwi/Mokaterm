using System.Text;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Elevation;
using Mokaterm.Modules.Ssh.Shell;
using Mokaterm.Modules.Ssh.Tests.Fakes;
using Mokaterm.Modules.Ssh.Tools;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>
/// Stdin and live output through the one place every exec goes: a request's input reaches the command as the login and
/// through sudo (behind the password line and the marker), and a long command is heard while it runs, as lines a view
/// can draw, with sudo's own words left out.
/// </summary>
public sealed class DockerLiveOutputTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Feature_WritesTheRequestsInputOnStdin()
	{
		string shell = Shell();
		SshCommandFeature feature = new((command, input, _, token) => LocalExecChannel.StartAsync(shell, PosixShellLocator.UnixPathPrefix, command, input, token), TimeProvider.System);
		using SecretBuffer secret = SecretBuffer.FromString("s3cr3t-token");

		RemoteCommandOutcome withInput = await feature.RunAsync(new RemoteCommandRequest { Command = "cat", Input = secret }, Ct).WaitAsync(TimeSpan.FromSeconds(30), Ct);
		RemoteCommandOutcome without = await feature.RunAsync(new RemoteCommandRequest { Command = "cat" }, Ct).WaitAsync(TimeSpan.FromSeconds(30), Ct);

		Assert.Equal("s3cr3t-token\n", withInput.Output.ToText());
		Assert.Equal("", without.Output.ToText());
		Assert.DoesNotContain("s3cr3t", new RemoteCommandRequest { Command = "cat", Input = secret }.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task Feature_LetsTheListenerHearOutputBeforeTheCommandEnds()
	{
		string shell = Shell();
		SshCommandFeature feature = new((command, input, _, token) => LocalExecChannel.StartAsync(shell, PosixShellLocator.UnixPathPrefix, command, input, token), TimeProvider.System);
		DockerOutputLines lines = new(errorIsProgress: true);
		string gate = Path.Combine(Path.GetTempPath(), "mokaterm-live-" + Guid.NewGuid().ToString("N"));
		try
		{
			Task<RemoteCommandOutcome> running = feature.RunAsync(
				new RemoteCommandRequest
				{
					Command = $"printf 'first\\n'; printf 'progress\\n' >&2; while [ ! -e {PosixShell.Quote(gate.Replace('\\', '/'))} ]; do sleep 0.1; done; printf 'last\\n'",
					OnOutput = lines.Write,
				},
				Ct);

			await Eventually.TrueAsync(() => lines.Snapshot().Count >= 2, "the first lines are heard");
			Assert.False(running.IsCompleted);
			await File.WriteAllTextAsync(gate, "", Ct);
			RemoteCommandOutcome outcome = await running.WaitAsync(TimeSpan.FromSeconds(30), Ct);
			lines.Complete();

			Assert.Equal(0, outcome.ExitCode);
			Assert.Equal(["first", "last", "progress"], lines.Snapshot().Select(line => line.Text).Order(StringComparer.Ordinal));
			Assert.DoesNotContain(lines.Snapshot(), line => line.IsError);
		}
		finally
		{
			File.Delete(gate);
		}
	}

	[Fact]
	public async Task UserShell_HandsTheCallsInputAndListenerToTheRequest()
	{
		RecordingCommands commands = new();
		UserDockerShell shell = new(commands);
		using SecretBuffer password = SecretBuffer.FromString("x");
		DockerOutputLines lines = new(errorIsProgress: false);

		_ = await shell.RunAsync(DockerRegistryCommands.Login("ghcr.io", "alice", password) with { OnOutput = lines.Write }, Ct);

		RemoteCommandRequest request = Assert.Single(commands.Requests);
		Assert.Same(password, request.Input);
		Assert.NotNull(request.OnOutput);
		Assert.Equal("docker login --username=alice --password-stdin ghcr.io", request.Command);
	}

	[Fact]
	public async Task SudoShell_PutsTheInputAfterThePasswordLine_AndTheScriptSkipsToTheMarker()
	{
		using LocalSudoShell sudo = new(Shell());
		using SudoRunner runner = sudo.Runner();
		SudoDockerShell shell = new(runner, TimeProvider.System);
		using SecretBuffer password = SecretBuffer.FromString("registry-s3cr3t");
		DockerOutputLines lines = new(errorIsProgress: false);
		DockerCall call = DockerCall.Script("cat; echo 'warn from the command' >&2", []) with { Input = password, OnOutput = lines.Write };

		DockerOutput output = await shell.RunAsync(call, Ct).WaitAsync(TimeSpan.FromSeconds(30), Ct);
		lines.Complete();

		Assert.True(output.Succeeded, output.Error);
		Assert.Equal("registry-s3cr3t\n", output.Output);
		Assert.Equal("warn from the command\n", output.Error);
		LocalSudoShell.Call sent = Assert.Single(sudo.Calls);
		Assert.Equal($"{LocalSudoShell.Password}\n{AdminStdin}\nregistry-s3cr3t", sent.Input);
		Assert.DoesNotContain("registry-s3cr3t", sent.Command, StringComparison.Ordinal);
		Assert.Equal(["registry-s3cr3t", "warn from the command"], lines.Snapshot().Select(line => line.Text).Order(StringComparer.Ordinal));
		Assert.DoesNotContain(lines.Snapshot(), line => line.Text.Contains(RemoteScripts.ElevatedMarker, StringComparison.Ordinal));
	}

	[Fact]
	public void Gate_DropsWhatSudoSaid_EvenWithTheMarkerSplitAcrossReads()
	{
		List<(CommandStream Stream, string Text)> heard = [];
		ElevatedOutputGate gate = new((stream, bytes) => heard.Add((stream, Encoding.UTF8.GetString(bytes))));

		gate.Write(CommandStream.Error, "[sudo] lecture\nMOKATERM-ELE"u8);
		gate.Write(CommandStream.Output, "out\n"u8);
		gate.Write(CommandStream.Error, "VATED\nreal error\n"u8);
		gate.Write(CommandStream.Error, "more\n"u8);

		Assert.Equal([(CommandStream.Output, "out\n"), (CommandStream.Error, "real error\n"), (CommandStream.Error, "more\n")], heard);
	}

	[Fact]
	public void Gate_GivesUpOnAStderrThatNeverReachesTheMarker()
	{
		int heard = 0;
		ElevatedOutputGate gate = new((_, _) => heard++);

		gate.Write(CommandStream.Error, new byte[70 * 1024]);
		gate.Write(CommandStream.Error, "MOKATERM-ELEVATED\nlate\n"u8);

		Assert.Equal(0, heard);
	}

	[Fact]
	public void Lines_AreCutAtEveryBreak_WithACharacterSplitBetweenReadsKeptWhole()
	{
		DockerOutputLines lines = new(errorIsProgress: true);
		byte[] euro = Encoding.UTF8.GetBytes("€");

		lines.Write(CommandStream.Error, "#5 [2/4] RUN make\r\n#5 0.1 bu"u8);
		lines.Write(CommandStream.Error, [.. "ilt "u8, euro[0]]);
		lines.Write(CommandStream.Error, [euro[1], euro[2], (byte)'\n']);
		lines.Write(CommandStream.Error, [.. "step 1\rstep 2\n#5 ERROR: process did not complete successfully\n"u8, 0x1b, .. "[31mERROR: failed to solve"u8, 0x1b, .. "[0m\n"u8]);
		lines.Write(CommandStream.Output, "sha256:abc"u8);

		IReadOnlyList<LogLine> snapshot = lines.Snapshot();
		Assert.Equal(["#5 [2/4] RUN make", "#5 0.1 built €", "step 1", "step 2", "#5 ERROR: process did not complete successfully", "ERROR: failed to solve", "sha256:abc"], snapshot.Select(line => line.Text));
		Assert.Equal([false, false, false, false, true, true, false], snapshot.Select(line => line.IsError));
		Assert.Equal("ERROR: failed to solve", lines.LastError());
	}

	[Fact]
	public void Lines_KeepTheNewestOnly_AndCutAVeryLongLine()
	{
		DockerOutputLines lines = new(errorIsProgress: false);
		for (int index = 0; index < LogTail.MaxLines + 25; index++)
		{
			lines.Write(CommandStream.Output, Encoding.UTF8.GetBytes($"line {index}\n"));
		}

		lines.Write(CommandStream.Error, Encoding.UTF8.GetBytes(new string('x', (DockerOutputLines.MaxLineCharacters * 2) + 10)));
		lines.Complete();

		IReadOnlyList<LogLine> snapshot = lines.Snapshot();
		Assert.Equal(LogTail.MaxLines, snapshot.Count);
		Assert.Equal(28, lines.Dropped);
		Assert.All(snapshot, line => Assert.True(line.Text.Length <= DockerOutputLines.MaxLineCharacters));
		Assert.Equal(10, snapshot[^1].Text.Length);
		Assert.True(snapshot[^1].IsError);
	}

	private const string AdminStdin = Mokaterm.Modules.Ssh.Admin.AdminScriptText.InputMarker;

	private static string Shell()
	{
		string? shell = PosixShellLocator.Find();
		if (shell is null)
		{
			Assert.Skip("No POSIX sh is available on this machine.");
		}

		return shell!;
	}

	/// <summary>Takes every request and answers each with nothing, as a command that printed nothing.</summary>
	private sealed class RecordingCommands : IRemoteCommandFeature
	{
		public List<RemoteCommandRequest> Requests { get; } = [];

		public Task<RemoteCommandOutcome> RunAsync(RemoteCommandRequest request, CancellationToken cancellationToken)
		{
			Requests.Add(request);
			return Task.FromResult(new RemoteCommandOutcome { ExitCode = 0, Output = CapturedOutput.Empty, Error = CapturedOutput.Empty });
		}
	}
}
