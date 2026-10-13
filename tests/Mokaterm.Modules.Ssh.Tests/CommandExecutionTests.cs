using System.Collections.Concurrent;
using System.Text;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Shell;
using Mokaterm.Modules.Ssh.Tests.Fakes;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>
/// The exec side of a command run over a fake channel: exit statuses and signals, both streams read side by side and
/// capped, the 64 MB ceiling, a dropped connection, cancellation, the channel closed on every way out, and nothing left
/// faulting unobserved.
/// </summary>
public sealed class CommandExecutionTests
{
	private const int OneMegabyte = 1024 * 1024;

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task RunAsync_ReturnsTheExitStatusAndBothStreams()
	{
		using FakeExecChannel channel = new(Stream("hello\n"), Stream("warning\n"), exitStatus: 3);

		RemoteCommandOutcome outcome = await RunAsync(channel, "echo hello");

		Assert.Equal(3, outcome.ExitCode);
		Assert.Null(outcome.ExitSignal);
		Assert.Equal("hello\n", Text(outcome.Output));
		Assert.Equal("warning\n", Text(outcome.Error));
		Assert.False(outcome.Output.IsTruncated);
		Assert.True(outcome.Duration >= TimeSpan.Zero);
		Assert.True(channel.IsDisposed);
	}

	[Fact]
	public async Task RunAsync_ACommandEndedByASignal_HasNoExitStatusButSaysWhichSignal()
	{
		using FakeExecChannel channel = new(Stream(""), Stream(""), exitStatus: null, exitSignal: "KILL");

		RemoteCommandOutcome outcome = await RunAsync(channel, "sleep 100");

		Assert.Null(outcome.ExitCode);
		Assert.Equal("KILL", outcome.ExitSignal);
		Assert.Equal(0, outcome.Output.TotalBytes);
	}

	[Fact]
	public async Task RunAsync_KeepsTheStartOfEachStream_AndCountsTheRest()
	{
		byte[] stdout = Encoding.ASCII.GetBytes(new string('o', 3 * OneMegabyte));
		byte[] stderr = Encoding.ASCII.GetBytes(new string('e', 5000));
		using FakeExecChannel channel = new(new MemoryStream(stdout), new MemoryStream(stderr), exitStatus: 0);

		RemoteCommandOutcome outcome = await RunAsync(channel, "cat big", outputLimitBytes: 4096);

		Assert.Equal(0, outcome.ExitCode);
		Assert.Equal(4096, outcome.Output.Bytes.Length);
		Assert.Equal(stdout.Length, outcome.Output.TotalBytes);
		Assert.True(outcome.Output.IsTruncated);
		Assert.Equal(4096, outcome.Error.Bytes.Length);
		Assert.Equal(5000, outcome.Error.TotalBytes);
		Assert.True(outcome.Error.IsTruncated);
	}

	[Fact]
	public async Task RunAsync_NeverKeepsLessThanTheSmallestCap()
	{
		using FakeExecChannel channel = new(Stream(new string('x', 3000)), Stream(""), exitStatus: 0);

		RemoteCommandOutcome outcome = await RunAsync(channel, "cat", outputLimitBytes: 10);

		Assert.Equal(RemoteCommandRequest.MinOutputLimitBytes, outcome.Output.Bytes.Length);
	}

	[Fact]
	public async Task RunAsync_StopsACommandThatFloodsStdout_WithoutWaitingForStderr()
	{
		using BlockingStream stderr = new();
		using FakeExecChannel channel = new(new EndlessStream(), stderr, exitStatus: 0, execution: new TaskCompletionSource().Task);

		RemoteCommandException failure = await Assert.ThrowsAsync<RemoteCommandException>(() => RunAsync(channel, "yes"));

		Assert.Contains("more than 64 MB to stdout", failure.Message, StringComparison.Ordinal);
		Assert.True(channel.IsDisposed);
	}

	[Fact]
	public async Task OutputCapture_RefusesAStreamPastTheCeiling_AndSaysWhichStream()
	{
		using EndlessStream source = new();

		RemoteCommandException failure = await Assert.ThrowsAsync<RemoteCommandException>(
			() => OutputCapture.ReadAsync(source, 1024, OneMegabyte, "stderr", Ct));

		Assert.Contains("more than 1 MB to stderr", failure.Message, StringComparison.Ordinal);
		Assert.InRange(source.Served, OneMegabyte, 2L * OneMegabyte);
	}

	[Fact]
	public async Task RunAsync_AConnectionThatDrops_FailsWithAMessageForTheUser()
	{
		using FakeExecChannel channel = new(
			Stream("partial"),
			Stream(""),
			exitStatus: null,
			execution: Task.FromException(new SshConnectionException("An established connection was aborted.")));

		RemoteCommandException failure = await Assert.ThrowsAsync<RemoteCommandException>(() => RunAsync(channel, "uptime"));

		Assert.Equal("The connection closed before the command finished.", failure.Message);
		Assert.True(channel.IsDisposed);
	}

	[Fact]
	public async Task RunAsync_Cancelled_StopsWaiting_AndClosesTheChannel()
	{
		using FakeExecChannel channel = await CancelWhileReadingAsync();

		Assert.True(channel.IsDisposed);
	}

	[Fact]
	public async Task RunAsync_Cancelled_LeavesNoFailureUnobserved()
	{
		// The System and Docker views cancel their reads whenever they close or change page. Closing the channel then
		// faults the reads, and a task nobody looked at reached TaskScheduler.UnobservedTaskException (and the crash log).
		ConcurrentQueue<AggregateException> unobserved = [];
		void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
		{
			if (e.Exception.ToString().Contains(nameof(CommandExecution), StringComparison.Ordinal))
			{
				unobserved.Enqueue(e.Exception);
			}
		}

		TaskScheduler.UnobservedTaskException += OnUnobserved;
		try
		{
			using (await CancelWhileReadingAsync())
			{
			}

			for (int pass = 0; pass < 5; pass++)
			{
				await Task.Delay(20, Ct);
				GC.Collect();
				GC.WaitForPendingFinalizers();
			}
		}
		finally
		{
			TaskScheduler.UnobservedTaskException -= OnUnobserved;
		}

		Assert.Empty(unobserved);
	}

	[Fact]
	public async Task RunAsync_AChannelTheServerRefuses_FailsWithAMessageForTheUser()
	{
		RemoteCommandException failure = await Assert.ThrowsAsync<RemoteCommandException>(() => CommandExecution.RunAsync(
			_ => Task.FromException<IExecChannel>(new SshException("Channel open failed.")),
			new RemoteCommandRequest { Command = "uptime" },
			TimeProvider.System,
			Ct));

		Assert.Equal("The server did not start the command.", failure.Message);
	}

	[Fact]
	public async Task Feature_RefusesACommandThatCannotBeSent()
	{
		SshCommandFeature feature = new(_ => throw new InvalidOperationException("never reached"), TimeProvider.System);

		await Assert.ThrowsAsync<ArgumentException>(() => feature.RunAsync(new RemoteCommandRequest { Command = "  " }, Ct));
		await Assert.ThrowsAsync<ArgumentException>(() => feature.RunAsync(new RemoteCommandRequest { Command = "echo " + (char)0 }, Ct));
	}

	[Fact]
	public async Task Feature_OnAClosedSession_SaysSoInItsOwnWords()
	{
		SshCommandFeature feature = new(
			_ => Task.FromException<SshClient>(new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, "The SSH connection is closed.")),
			TimeProvider.System);

		RemoteCommandException failure = await Assert.ThrowsAsync<RemoteCommandException>(
			() => feature.RunAsync(new RemoteCommandRequest { Command = "uptime" }, Ct));

		Assert.Equal("The SSH connection is closed.", failure.Message);
	}

	/// <summary>Starts a command that prints nothing and cancels it while both streams are being read.</summary>
	private static async Task<FakeExecChannel> CancelWhileReadingAsync()
	{
		BlockingStream stdout = new();
		FakeExecChannel channel = new(stdout, new BlockingStream(), exitStatus: null, execution: new TaskCompletionSource().Task);
		using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);

		Task<RemoteCommandOutcome> running = CommandExecution.RunAsync(
			_ => Task.FromResult<IExecChannel>(channel),
			new RemoteCommandRequest { Command = "sleep 100" },
			TimeProvider.System,
			cancel.Token);
		await Eventually.TrueAsync(() => stdout.Reading, "the command is being read");
		await cancel.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
		return channel;
	}

	private static Task<RemoteCommandOutcome> RunAsync(FakeExecChannel channel, string command, int outputLimitBytes = 256 * 1024) =>
		CommandExecution.RunAsync(
			_ => Task.FromResult<IExecChannel>(channel),
			new RemoteCommandRequest { Command = command, OutputLimitBytes = outputLimitBytes },
			TimeProvider.System,
			Ct);

	private static MemoryStream Stream(string text) => new(Encoding.UTF8.GetBytes(text));

	private static string Text(CapturedOutput output) => Encoding.UTF8.GetString(output.Bytes.Span);
}
