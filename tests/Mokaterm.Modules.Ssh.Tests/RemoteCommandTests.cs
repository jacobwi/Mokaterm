using System.Text;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.Shell;
using Mokaterm.Modules.Ssh.Tests.Fakes;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>
/// How a script's command is read to its end: stdout whole up to its cap, stderr side by side with it, its start kept
/// and marked cut and the rest read and dropped, and the channel closed on every way out.
/// </summary>
public sealed class RemoteCommandTests
{
	private const int OneMegabyte = 1024 * 1024;

	private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ReadAllAsync_ReturnsEverything_UpToTheLimit()
	{
		byte[] data = new byte[OneMegabyte];
		Random.Shared.NextBytes(data);
		using MemoryStream source = new(data);

		byte[] read = await RemoteCommand.ReadAllAsync(source, data.Length, Ct).WaitAsync(Limit, Ct);

		Assert.Equal(data, read);
	}

	[Fact]
	public async Task ReadAllAsync_StopsOutputThatNeverEnds()
	{
		using EndlessStream source = new();

		RemoteFileSystemException failure = await Assert.ThrowsAsync<RemoteFileSystemException>(
			() => RemoteCommand.ReadAllAsync(source, OneMegabyte, Ct).WaitAsync(Limit, Ct));

		Assert.Contains("more than 1 MB", failure.Message, StringComparison.Ordinal);
		Assert.InRange(source.Served, OneMegabyte, 2L * OneMegabyte);
	}

	[Fact]
	public async Task ReadToEnd_ReturnsStdoutWhole_AndStderrWhole_WhenItIsSmall()
	{
		using FakeExecChannel channel = FakeExecChannel.Finished([.. "listing\n"u8], "warning: odd file\n", exitStatus: 1);

		RemoteCommandResult result = await CommandExecution.ReadToEndAsync(channel, OneMegabyte, Ct).WaitAsync(Limit, Ct);

		Assert.Equal(1, result.ExitStatus);
		Assert.Equal("listing\n", Encoding.UTF8.GetString(result.Output));
		Assert.Equal("warning: odd file\n", result.Error);
		Assert.False(result.ErrorTruncated);
		Assert.True(channel.IsDisposed);
	}

	[Fact]
	public async Task ReadToEnd_KeepsTheStartOfStderr_MarksItCut_AndReadsTheRest()
	{
		const long Sent = (3L * RemoteCommand.MaxErrorBytes) + 17;
		EndlessStream stderr = new(Sent);
		using FakeExecChannel channel = new(new MemoryStream([.. "done\n"u8]), stderr, exitStatus: 0);

		RemoteCommandResult result = await CommandExecution.ReadToEndAsync(channel, OneMegabyte, Ct).WaitAsync(Limit, Ct);

		Assert.Equal(0, result.ExitStatus);
		Assert.Equal(RemoteCommand.MaxErrorBytes, result.Error.Length);
		Assert.True(result.ErrorTruncated);

		// Read to its end, not left to pile up in SSH.NET's buffer.
		Assert.Equal(Sent, stderr.Served);
		Assert.Equal("done\n", Encoding.UTF8.GetString(result.Output));
	}

	[Fact]
	public async Task ReadToEnd_StopsACommandThatFloodsStderr()
	{
		EndlessStream stderr = new();
		using FakeExecChannel channel = new(new BlockingStream(), stderr, exitStatus: null, execution: new TaskCompletionSource().Task);

		RemoteFileSystemException failure = await Assert.ThrowsAsync<RemoteFileSystemException>(
			() => CommandExecution.ReadToEndAsync(channel, OneMegabyte, Ct).WaitAsync(Limit, Ct));

		Assert.Contains("more than 64 MB to stderr", failure.Message, StringComparison.Ordinal);
		Assert.InRange(stderr.Served, RemoteCommand.MaxOutputBytes, RemoteCommand.MaxOutputBytes + (2L * RemoteCommand.CopyBufferBytes));
		Assert.True(channel.IsDisposed);
	}

	[Fact]
	public async Task ReadToEnd_StopsACommandThatFloodsStdout_WithoutWaitingForStderr()
	{
		using FakeExecChannel channel = new(new EndlessStream(), new BlockingStream(), exitStatus: null, execution: new TaskCompletionSource().Task);

		RemoteFileSystemException failure = await Assert.ThrowsAsync<RemoteFileSystemException>(
			() => CommandExecution.ReadToEndAsync(channel, OneMegabyte, Ct).WaitAsync(Limit, Ct));

		Assert.Contains("more than 1 MB", failure.Message, StringComparison.Ordinal);
		Assert.True(channel.IsDisposed);
	}

	[Fact]
	public async Task ReadToEnd_Cancelled_StopsWaiting_AndClosesTheChannel()
	{
		BlockingStream stdout = new();
		using FakeExecChannel channel = new(stdout, new BlockingStream(), exitStatus: null, execution: new TaskCompletionSource().Task);
		using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);

		Task<RemoteCommandResult> running = CommandExecution.ReadToEndAsync(channel, OneMegabyte, cancel.Token);
		await Eventually.TrueAsync(() => stdout.Reading, "the command is being read");
		await cancel.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(Limit, Ct));
		Assert.True(channel.IsDisposed);
	}

	[Fact]
	public async Task ReadToEnd_AConnectionThatDrops_PassesSshNetsFailureOn()
	{
		using FakeExecChannel channel = new(
			new MemoryStream([.. "partial"u8]),
			new MemoryStream(),
			exitStatus: null,
			execution: Task.FromException(new Renci.SshNet.Common.SshConnectionException("An established connection was aborted.")));

		await Assert.ThrowsAsync<Renci.SshNet.Common.SshConnectionException>(() => CommandExecution.ReadToEndAsync(channel, OneMegabyte, Ct).WaitAsync(Limit, Ct));
		Assert.True(channel.IsDisposed);
	}
}
