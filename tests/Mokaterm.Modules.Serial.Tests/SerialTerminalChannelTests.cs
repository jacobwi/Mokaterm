using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Serial.Protocol;
using Mokaterm.Modules.Serial.Sessions;
using Mokaterm.Modules.Serial.Tests.Fakes;

namespace Mokaterm.Modules.Serial.Tests;

/// <summary>
/// The channel over a stream the test drives, so every path except the driver itself is covered: what arrives, what
/// goes out, the pins, and the two ways a line ends.
/// </summary>
public sealed class SerialTerminalChannelTests
{
	private const int TestTimeoutMilliseconds = 30_000;

	private static readonly TimeSpan DeviceCheck = TimeSpan.FromSeconds(2);

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task Read_GivesWhatTheDeviceSent_Untouched()
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		await using Harness harness = Harness.Start();

		// A serial line is bytes: an escape sequence, a 0xFF and a NUL are all data and none of them are framing.
		byte[] sent = [0x1B, (byte)'[', (byte)'2', (byte)'J', 0xFF, 0x00, 0x80];
		harness.Link.Device.Send(sent);

		Assert.Equal(sent, await harness.ReadAsync(sent.Length, token));
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task Write_SendsTheLineEndingTheConnectionUses()
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		await using Harness harness = Harness.Start(lineEnding: SerialLineEnding.CrLf);

		await harness.Channel.WriteAsync("a\r"u8.ToArray(), token);

		Assert.Equal("a\r\n"u8.ToArray(), await harness.Link.Device.WaitForWrittenAsync(3, token));
		Assert.True(harness.Link.Device.Flushes > 0);
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task Write_WithLocalEcho_ShowsWhatWasTyped()
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		await using Harness harness = Harness.Start(localEcho: true);

		await harness.Channel.WriteAsync("hi\r"u8.ToArray(), token);

		Assert.Equal("hi\r\n"u8.ToArray(), await harness.ReadAsync(4, token));
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task Write_WithoutLocalEcho_ShowsNothing()
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		await using Harness harness = Harness.Start();

		await harness.Channel.WriteAsync("hi"u8.ToArray(), token);

		await harness.AssertNothingArrivesAsync(token);
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task ACodePage_IsTranscodedBothWays()
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		await using Harness harness = Harness.Start(encoding: TerminalEncodings.Resolve("windows-1252"));

		// 0xDC is U+00DC on the line and two bytes in the view.
		harness.Link.Device.Send(0xDC);
		Assert.Equal("Ü"u8.ToArray(), await harness.ReadAsync(2, token));

		await harness.Channel.WriteAsync("Ü"u8.ToArray(), token);
		Assert.Equal(new byte[] { 0xDC }, await harness.Link.Device.WaitForWrittenAsync(1, token));
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task AReadThatTimesOut_IsSilenceRatherThanAFailure()
	{
		CancellationToken token = TestContext.Current.CancellationToken;

		// Asked for before the loop starts, so the first read really is the one that times out.
		await using Harness harness = Harness.Start(timeoutFirstRead: true);

		harness.Link.Device.Send("still here"u8.ToArray());

		Assert.Equal("still here"u8.ToArray(), await harness.ReadAsync(10, token));
		Assert.False(harness.Channel.Ended.IsCompleted);
		Assert.Null(harness.Channel.Failure);
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task ADeviceThatGoesAway_EndsTheChannelSoTheSessionShowsAsDropped()
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		await using Harness harness = Harness.Start();

		harness.Link.Device.Unplug();

		await Eventually.CompletesAsync(harness.Channel.Ended);
		Assert.IsType<IOException>(harness.Channel.Failure);
		Assert.False(harness.Channel.Status.IsOpen);

		// A read after the end returns 0, which is what tells the view the stream is over.
		Assert.Equal(0, await harness.Channel.ReadAsync(new byte[16], token));
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task AClosedPort_EndsTheChannelWithoutAFailure()
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		await using Harness harness = Harness.Start();

		harness.Link.Device.CloseLine();

		await Eventually.CompletesAsync(harness.Channel.Ended);
		Assert.Null(harness.Channel.Failure);
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task TheDeviceCheck_NoticesAnUnpluggedAdapterWhileTheLineIsQuiet()
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		await using Harness harness = Harness.Start();

		// Nothing is arriving and nothing is being typed: the pins are the only thing left that can tell.
		harness.Link.SignalFailure = new IOException("The device is not connected.");
		await harness.AdvanceUntilAsync(() => harness.Channel.Ended.IsCompleted, "the channel notices the port is gone", token);

		await Eventually.CompletesAsync(harness.Channel.Ended);
		SerialProtocolException failure = Assert.IsType<SerialProtocolException>(harness.Channel.Failure);
		Assert.Contains(harness.Link.PortName, failure.Message, StringComparison.Ordinal);

		// Closing the port is also what lets go of a read waiting inside the driver.
		Assert.True(harness.Link.DisposeCount > 0);
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task Resize_SendsNothing_BecauseASerialLineCarriesNoWindowSize()
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		await using Harness harness = Harness.Start();

		await harness.Channel.ResizeAsync(new TerminalSize(120, 40), token);

		Assert.Empty(harness.Link.Device.Written);
		await harness.AssertNothingArrivesAsync(token);
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task Status_DescribesTheLineTheSessionIsRunningOn()
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		await using Harness harness = Harness.Start(localEcho: true, lineEnding: SerialLineEnding.Lf);

		SerialPortStatus status = harness.Channel.Status;

		Assert.Equal("COM-TEST", status.PortName);
		Assert.Equal("115200 8N1", status.Line.Frame);
		Assert.True(status.IsOpen);
		Assert.True(status.CanSetRts);
		Assert.True(status.ReportsPins);
		Assert.True(status.LocalEcho);
		Assert.Equal(SerialLineEnding.Lf, status.LineEnding);
		Assert.Equal("utf-8", status.EncodingName);
		Assert.Equal(0, status.WriteTimeouts);

		// The pins were read once as the channel started, so the panel has something to show at once.
		await Eventually.TrueAsync(() => harness.Link.SignalReads > 0, "the pins have been read");
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task DtrAndRts_AreDrivenAndReported()
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		await using Harness harness = Harness.Start();
		int changes = 0;
		harness.Channel.Changed += () => Interlocked.Increment(ref changes);

		await harness.Channel.SetDtrAsync(false, token);
		Assert.False(harness.Channel.Status.Signals.Dtr);

		await harness.Channel.SetRtsAsync(false, token);
		Assert.False(harness.Channel.Status.Signals.Rts);

		Assert.True(Volatile.Read(ref changes) >= 2);
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task Rts_IsRefusedWhileFlowControlDrivesIt()
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		await using Harness harness = Harness.Start(flowControl: SerialFlowControl.RtsCts);

		Assert.False(harness.Channel.Status.CanSetRts);
		await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Channel.SetRtsAsync(false, token));
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task ABreak_HoldsTheLineAndThenReleasesIt()
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		await using Harness harness = Harness.Start();

		Task breaking = harness.Channel.SendBreakAsync(token);
		await Eventually.TrueAsync(() => harness.Channel.Status.Signals.Break, "the line is held low");

		await harness.AdvanceUntilAsync(() => breaking.IsCompleted, "the break ends", token);
		await Eventually.CompletesAsync(breaking);
		Assert.False(harness.Channel.Status.Signals.Break);
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task AWriteTheDeviceDoesNotTake_IsCountedRatherThanEndingTheSession()
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		await using Harness harness = Harness.Start();
		harness.Link.Device.TimeoutWrites = true;

		await harness.Channel.WriteAsync("x"u8.ToArray(), token);

		Assert.Equal(1, harness.Channel.Status.WriteTimeouts);
		Assert.False(harness.Channel.Ended.IsCompleted);
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task Dispose_ClosesThePortAndEndsTheStream()
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		Harness harness = Harness.Start();

		await harness.DisposeAsync();

		Assert.Equal(1, harness.Link.DisposeCount);
		await Eventually.CompletesAsync(harness.Channel.Ended);
		Assert.Equal(0, await harness.Channel.ReadAsync(new byte[16], token));
	}

	private sealed class Harness : IAsyncDisposable
	{
		private Harness(FakeSerialLink link, SerialTerminalChannel channel, FakeTimeProvider time)
		{
			Link = link;
			Channel = channel;
			Time = time;
		}

		public FakeSerialLink Link { get; }

		public SerialTerminalChannel Channel { get; }

		public FakeTimeProvider Time { get; }

		public static Harness Start(
			Encoding? encoding = null,
			SerialLineEnding lineEnding = SerialLineEnding.Cr,
			bool localEcho = false,
			SerialFlowControl flowControl = SerialFlowControl.None,
			bool timeoutFirstRead = false)
		{
			FakeSerialLink link = new(SerialLineSettings.Default with { FlowControl = flowControl })
			{
				CanSetRts = flowControl is not (SerialFlowControl.RtsCts or SerialFlowControl.RtsCtsXOnXOff),
			};

			link.Device.TimeoutNextRead = timeoutFirstRead;
			FakeTimeProvider time = new();
			SerialTerminalChannel channel = new(
				link,
				new SerialChannelOptions
				{
					Encoding = encoding ?? Encoding.UTF8,
					LineEnding = lineEnding,
					LocalEcho = localEcho,
					DeviceCheck = DeviceCheck,
					Break = TimeSpan.FromMilliseconds(250),
					TimeProvider = time,
				},
				NullLogger<SerialTerminalChannel>.Instance);
			channel.Start();
			return new Harness(link, channel, time);
		}

		/// <summary>Reads until <paramref name="count"/> bytes have arrived, or the stream ends.</summary>
		public async Task<byte[]> ReadAsync(int count, CancellationToken cancellationToken)
		{
			List<byte> received = [];
			byte[] buffer = new byte[256];
			while (received.Count < count)
			{
				int read = await Channel.ReadAsync(buffer, cancellationToken);
				if (read == 0)
				{
					break;
				}

				received.AddRange(buffer.AsSpan(0, read));
			}

			return [.. received];
		}

		public async Task AssertNothingArrivesAsync(CancellationToken cancellationToken)
		{
			using CancellationTokenSource quiet = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			quiet.CancelAfter(Eventually.Quiet);
			await Assert.ThrowsAnyAsync<OperationCanceledException>(
				async () => await Channel.ReadAsync(new byte[16], quiet.Token));
		}

		/// <summary>
		/// Steps fake time until <paramref name="condition"/> holds. Stepping copes with a timer the code under test
		/// registers a moment after the test starts advancing.
		/// </summary>
		public async Task AdvanceUntilAsync(Func<bool> condition, string description, CancellationToken cancellationToken, int maxSteps = 40)
		{
			for (int step = 0; step < maxSteps && !condition(); step++)
			{
				Time.Advance(DeviceCheck);
				await Task.Delay(5, cancellationToken);
			}

			await Eventually.TrueAsync(condition, description);
		}

		public ValueTask DisposeAsync() => Channel.DisposeAsync();
	}
}
