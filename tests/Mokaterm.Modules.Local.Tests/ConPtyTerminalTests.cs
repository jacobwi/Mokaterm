using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Local.Pty;
using Mokaterm.Modules.Local.Sessions;
using Mokaterm.Modules.Local.Shells;
using Mokaterm.Modules.Local.Tests.Fakes;

namespace Mokaterm.Modules.Local.Tests;

/// <summary>
/// A real pseudo console with the Command Prompt in it, which every Windows has. These run wherever ConPTY exists and
/// skip elsewhere; every wait is bounded, and every terminal is disposed even when an assertion fails.
/// </summary>
public sealed class ConPtyTerminalTests
{
	private static readonly string Cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");


	[Fact(Timeout = TestTimeouts.NetworkMilliseconds)]
	public async Task AProgramThatExits_DeliversAllItsOutput_AndItsExitCode()
	{
		SkipUnlessSupported();
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		RecordingOutput output = new();
		await using IPseudoTerminal terminal = PseudoTerminalPlatform.Start(Launch("/c echo mokaterm-conpty-output& exit /b 3"), new TerminalSize(100, 30), output);

		int? exitCode = await terminal.Exited.WaitAsync(Eventually.Limit, cancellationToken);

		Assert.Equal(3, exitCode);
		Assert.True(terminal.ProcessId > 0);
		Assert.Contains("mokaterm-conpty-output", output.Text, StringComparison.Ordinal);
	}

	[Fact(Timeout = TestTimeouts.NetworkMilliseconds)]
	public async Task AnInteractiveShell_TakesInputAndResizes_AndEndsWithExit()
	{
		SkipUnlessSupported();
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		RecordingOutput output = new();
		await using IPseudoTerminal terminal = PseudoTerminalPlatform.Start(
			Launch("/k", ("MOKATERM_PROBE", "probe-4711")),
			new TerminalSize(100, 30),
			output);

		terminal.Resize(new TerminalSize(80, 24));
		terminal.Resize(new TerminalSize(0, 0));

		// The substitution prints a word the typed line does not hold, so only the shell's answer can match, and it
		// can only answer with a variable that reached its environment.
		await terminal.WriteAsync(Encoding.UTF8.GetBytes("echo %MOKATERM_PROBE:4711=seen%\r"), cancellationToken);
		await Eventually.TrueAsync(() => output.Text.Contains("probe-seen", StringComparison.Ordinal), "the shell answered the typed command");

		await terminal.WriteAsync(Encoding.UTF8.GetBytes("exit 0\r"), cancellationToken);

		Assert.Equal(0, await terminal.Exited.WaitAsync(Eventually.Limit, cancellationToken));
	}

	[Fact(Timeout = TestTimeouts.NetworkMilliseconds)]
	public async Task Dispose_EndsTheProgram_AndEverythingItStarted()
	{
		SkipUnlessSupported();
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		HashSet<int> before = [.. ProcessIds("waitfor")];
		IPseudoTerminal terminal = PseudoTerminalPlatform.Start(
			Launch($"/k waitfor /t 600 Mokaterm{Guid.NewGuid():N}"),
			new TerminalSize(100, 30),
			new RecordingOutput());
		try
		{
			using Process shell = Process.GetProcessById(terminal.ProcessId);
			int child = 0;
			await Eventually.TrueAsync(
				() => (child = ProcessIds("waitfor").FirstOrDefault(id => !before.Contains(id))) != 0,
				"the shell started its child");
			using Process started = Process.GetProcessById(child);

			await terminal.DisposeAsync().AsTask().WaitAsync(Eventually.Limit, cancellationToken);

			Assert.Null(await terminal.Exited.WaitAsync(Eventually.Limit, cancellationToken));
			await shell.WaitForExitAsync(cancellationToken).WaitAsync(Eventually.Limit, cancellationToken);
			await started.WaitForExitAsync(cancellationToken).WaitAsync(Eventually.Limit, cancellationToken);
		}
		finally
		{
			await terminal.DisposeAsync();
		}
	}

	[Fact(Timeout = TestTimeouts.NetworkMilliseconds)]
	public async Task Dispose_WhileNobodyReadsTheOutput_StillEndsTheShell()
	{
		SkipUnlessSupported();
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		LocalTerminalChannel channel = new("Command Prompt", reportExitCode: true, NullLogger.Instance);
		IPseudoTerminal terminal = PseudoTerminalPlatform.Start(
			Launch("/c for /l %i in (1,1,200000) do @echo line %i of a flood nobody reads"),
			new TerminalSize(120, 30),
			channel);
		channel.Attach(terminal);
		try
		{
			using Process shell = Process.GetProcessById(terminal.ProcessId);

			// The queue fills and the shell stalls behind it, which is the state a closed tab finds a busy shell in.
			await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
			Assert.False(shell.HasExited);

			await channel.DisposeAsync().AsTask().WaitAsync(Eventually.Limit, cancellationToken);

			await shell.WaitForExitAsync(cancellationToken).WaitAsync(Eventually.Limit, cancellationToken);
			Assert.Null(await terminal.Exited.WaitAsync(Eventually.Limit, cancellationToken));
		}
		finally
		{
			await channel.DisposeAsync();
		}
	}

	private static void SkipUnlessSupported()
	{
		if (!PseudoTerminalPlatform.IsSupported)
		{
			Assert.Skip("The Windows pseudo console needs Windows 10 version 1809 or later.");
		}
	}

	private static PseudoTerminalLaunch Launch(string arguments, params (string Name, string Value)[] variables)
	{
		Dictionary<string, string> environment = new(LocalMachine.Current().Variables, StringComparer.OrdinalIgnoreCase);
		foreach ((string name, string value) in variables)
		{
			environment[name] = value;
		}

		return new PseudoTerminalLaunch
		{
			Program = Cmd,
			ExtraArguments = arguments,
			WorkingDirectory = Path.GetTempPath(),
			Environment = environment,
		};
	}

	private static int[] ProcessIds(string name)
	{
		Process[] processes = Process.GetProcessesByName(name);
		try
		{
			return [.. processes.Select(process => process.Id)];
		}
		finally
		{
			foreach (Process process in processes)
			{
				process.Dispose();
			}
		}
	}
}
