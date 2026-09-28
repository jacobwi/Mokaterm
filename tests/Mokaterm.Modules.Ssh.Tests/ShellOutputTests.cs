using Mokaterm.Modules.Ssh.Terminal;
using Mokaterm.Modules.Ssh.Tests.Fakes;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>
/// The shell output path under a server that floods: a producer thread stands in for SSH.NET's message loop, filling a
/// buffer that grows without limit and calling Throttle after each packet, exactly as the channel wires it.
/// </summary>
public sealed class ShellOutputTests
{
	private const int PacketBytes = 32 * 1024;

	private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Flood_WithNobodyReading_StopsTheProducerAtABoundedBacklog()
	{
		FakeShellBuffer shell = new();
		ShellOutput output = new(shell.Read, () => shell.Length, "test shell reader");
		using Flood flood = new(shell, output);

		long settled = await Eventually.StableAsync(() => flood.Produced, stablePolls: 8);

		// Everything the queue holds, everything the shell may hold before the producer waits, and the packet in flight.
		long bound = (ShellOutput.QueueCapacity * (long)ShellOutput.ChunkBytes) + ShellOutput.MaxBacklogBytes + (2L * PacketBytes);
		Assert.InRange(settled, ShellOutput.MaxBacklogBytes, bound);
		Assert.True(shell.Length <= ShellOutput.MaxBacklogBytes + PacketBytes, "The shell's own buffer stays bounded too.");

		byte[] buffer = new byte[64 * 1024];
		long taken = 0;
		while (taken < 4L * ShellOutput.MaxBacklogBytes)
		{
			taken += await output.ReadAsync(buffer, Ct);
		}

		Assert.True(flood.Produced > settled, "Reading lets the producer go on.");
		output.Stop();
		await flood.StopAsync();
		shell.Close();
	}

	[Fact]
	public async Task Output_ArrivesInOrderWithoutLoss_WhileTheProducerIsHeldBack()
	{
		FakeShellBuffer shell = new();
		ShellOutput output = new(shell.Read, () => shell.Length, "test shell reader");
		byte[] expected = new byte[5 * 1024 * 1024];
		for (int i = 0; i < expected.Length; i++)
		{
			expected[i] = (byte)(i * 31 % 251);
		}

		Task producer = Task.Factory.StartNew(
			() =>
			{
				for (int offset = 0; offset < expected.Length; offset += PacketBytes)
				{
					shell.Append(expected.AsSpan(offset, Math.Min(PacketBytes, expected.Length - offset)));
					output.Throttle();
				}

				shell.Close();
			},
			Ct,
			TaskCreationOptions.LongRunning,
			TaskScheduler.Default);

		using MemoryStream received = new();
		byte[] buffer = new byte[7000];
		int read;
		while ((read = await output.ReadAsync(buffer, Ct)) > 0)
		{
			received.Write(buffer, 0, read);
		}

		await producer.WaitAsync(Limit, Ct);
		Assert.Equal(expected, received.ToArray());
		await output.Ended.WaitAsync(Limit, Ct);
	}

	[Fact]
	public async Task Stop_ReleasesAHeldProducer_AndEndsTheOutput()
	{
		FakeShellBuffer shell = new();
		ShellOutput output = new(shell.Read, () => shell.Length, "test shell reader");
		using Flood flood = new(shell, output);
		_ = await Eventually.StableAsync(() => flood.Produced, stablePolls: 8);

		output.Stop();

		await flood.StopAsync();
		await output.Ended.WaitAsync(Limit, Ct);
		shell.Close();
	}

	[Fact]
	public async Task AReadThatThrows_EndsTheOutputInsteadOfTheProcess()
	{
		ShellOutput output = new(_ => throw new InvalidOperationException("The channel broke in a new way."), () => 0, "test shell reader");

		await output.Ended.WaitAsync(Limit, Ct);

		Assert.Equal(0, await output.ReadAsync(new byte[16], Ct));
	}

	[Fact]
	public void Throttle_NeverThrows_WhenTheBacklogCannotBeRead()
	{
		FakeShellBuffer shell = new();
		ShellOutput output = new(shell.Read, () => throw new ObjectDisposedException("shell"), "test shell reader");

		output.Throttle();

		output.Stop();
		shell.Close();
	}

	/// <summary>A message loop that never runs dry: it appends packet after packet and throttles after each one.</summary>
	private sealed class Flood : IDisposable
	{
		private readonly CancellationTokenSource _stop = new();
		private readonly Task _loop;
		private long _produced;

		public Flood(FakeShellBuffer shell, ShellOutput output)
		{
			byte[] packet = new byte[PacketBytes];
			packet.AsSpan().Fill((byte)'y');
			CancellationToken stop = _stop.Token;
			_loop = Task.Factory.StartNew(
				() =>
				{
					while (!stop.IsCancellationRequested)
					{
						shell.Append(packet);
						_ = Interlocked.Add(ref _produced, packet.Length);
						output.Throttle();
					}
				},
				CancellationToken.None,
				TaskCreationOptions.LongRunning,
				TaskScheduler.Default);
		}

		public long Produced => Interlocked.Read(ref _produced);

		public async Task StopAsync()
		{
			await _stop.CancelAsync();
			await _loop.WaitAsync(Limit, TestContext.Current.CancellationToken);
		}

		public void Dispose()
		{
			_stop.Cancel();
			_stop.Dispose();
		}
	}
}
