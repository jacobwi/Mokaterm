using System.Text;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Shell;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>
/// The exec side of a command run over a fake channel: exit statuses and signals, both streams read side by side and
/// capped, the 64 MB ceiling, a dropped connection, cancellation, and the channel closed on every way out.
/// </summary>
public sealed class CommandExecutionTests
{
	private const int OneMegabyte = 1024 * 1024;

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task RunAsync_ReturnsTheExitStatusAndBothStreams()
	{
		using FakeChannel channel = new(Stream("hello\n"), Stream("warning\n"), exitStatus: 3);

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
		using FakeChannel channel = new(Stream(""), Stream(""), exitStatus: null, exitSignal: "KILL");

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
		using FakeChannel channel = new(new MemoryStream(stdout), new MemoryStream(stderr), exitStatus: 0);

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
		using FakeChannel channel = new(Stream(new string('x', 3000)), Stream(""), exitStatus: 0);

		RemoteCommandOutcome outcome = await RunAsync(channel, "cat", outputLimitBytes: 10);

		Assert.Equal(RemoteCommandRequest.MinOutputLimitBytes, outcome.Output.Bytes.Length);
	}

	[Fact]
	public async Task RunAsync_StopsACommandThatFloodsStdout_WithoutWaitingForStderr()
	{
		using BlockingStream stderr = new();
		using FakeChannel channel = new(new EndlessStream(), stderr, exitStatus: 0, execution: new TaskCompletionSource().Task);

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
		using FakeChannel channel = new(
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
		using BlockingStream stdout = new();
		using BlockingStream stderr = new();
		using FakeChannel channel = new(stdout, stderr, exitStatus: null, execution: new TaskCompletionSource().Task);
		using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);

		Task<RemoteCommandOutcome> running = CommandExecution.RunAsync(
			_ => Task.FromResult<IExecChannel>(channel),
			new RemoteCommandRequest { Command = "sleep 100" },
			TimeProvider.System,
			cancel.Token);
		await Eventually.TrueAsync(() => stdout.Reading, "the command is being read");
		await cancel.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
		Assert.True(channel.IsDisposed);
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

	private static Task<RemoteCommandOutcome> RunAsync(FakeChannel channel, string command, int outputLimitBytes = 256 * 1024) =>
		CommandExecution.RunAsync(
			_ => Task.FromResult<IExecChannel>(channel),
			new RemoteCommandRequest { Command = command, OutputLimitBytes = outputLimitBytes },
			TimeProvider.System,
			Ct);

	private static MemoryStream Stream(string text) => new(Encoding.UTF8.GetBytes(text));

	private static string Text(CapturedOutput output) => Encoding.UTF8.GetString(output.Bytes.Span);

	private sealed class FakeChannel : IExecChannel
	{
		private int _disposed;

		public FakeChannel(Stream output, Stream error, int? exitStatus, string? exitSignal = null, Task? execution = null)
		{
			Output = output;
			ErrorOutput = error;
			ExitStatus = exitStatus;
			ExitSignal = exitSignal;
			Execution = execution ?? Task.CompletedTask;
		}

		public Stream Output { get; }

		public Stream ErrorOutput { get; }

		public Task Execution { get; }

		public int? ExitStatus { get; }

		public string? ExitSignal { get; }

		public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) != 0)
			{
				return;
			}

			// Closing the channel ends the reads that are still waiting, as SSH.NET does.
			Output.Dispose();
			ErrorOutput.Dispose();
		}
	}

	/// <summary>A stream that says nothing until it is closed, like a command that runs and prints nothing.</summary>
	private sealed class BlockingStream : Stream
	{
		private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int _reading;

		public bool Reading => Volatile.Read(ref _reading) != 0;

		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			_ = Interlocked.Exchange(ref _reading, 1);
			await _closed.Task;
			throw new ObjectDisposedException(nameof(BlockingStream));
		}

		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		protected override void Dispose(bool disposing)
		{
			_ = _closed.TrySetResult();
			base.Dispose(disposing);
		}
	}

	/// <summary>A command that keeps printing, like <c>yes</c>.</summary>
	private sealed class EndlessStream : Stream
	{
		private long _served;

		public long Served => Interlocked.Read(ref _served);

		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			buffer.AsSpan(offset, count).Fill((byte)'y');
			_ = Interlocked.Add(ref _served, count);
			return count;
		}

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}
}
