using System.IO.Pipelines;
using Devolutions.IronRdp;
using Mokaterm.Modules.Rdp.Protocol;
using Mokaterm.Modules.Rdp.Tests.Fakes;
using Action = Devolutions.IronRdp.Action;

namespace Mokaterm.Modules.Rdp.Tests;

/// <summary>
/// The reader that replaced IronRDP's <c>Framed&lt;T&gt;</c>: whole messages however the wire cuts them, a token that
/// stops a read nobody answers, and no trust in a length the server announces.
/// </summary>
public sealed class RdpFrameReaderTests
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

	[Fact]
	public async Task ReadPduAsync_AMessageThatArrivesAByteAtATime_ComesOutWhole()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		Pipe pipe = new();
		using RdpFrameReader reader = new(pipe.Reader.AsStream());
		byte[] message = Tpkt(40, fill: 0x5A);

		ValueTask<(Action Action, byte[] Payload)> reading = reader.ReadPduAsync(cancellationToken);
		foreach (byte part in message)
		{
			await pipe.Writer.WriteAsync(new[] { part }, cancellationToken);
		}

		(Action action, byte[] payload) = await reading.AsTask().WaitAsync(Wait, cancellationToken);
		using (action)
		{
			Assert.Equal(message, payload);
		}
	}

	[Fact]
	public async Task ReadPduAsync_MessagesThatArriveTogether_ComeOutOneByOne()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		byte[] first = Tpkt(20, fill: 1);
		byte[] second = Tpkt(300, fill: 2);
		byte[] third = Tpkt(9000, fill: 3);
		using MemoryStream wire = new([.. first, .. second, .. third]);
		using RdpFrameReader reader = new(wire);

		foreach (byte[] expected in new[] { first, second, third })
		{
			(Action action, byte[] payload) = await reader.ReadPduAsync(cancellationToken);
			using (action)
			{
				Assert.Equal(expected, payload);
			}
		}

		Assert.Equal(0, reader.Buffered);
	}

	[Fact]
	public async Task ReadPduAsync_AServerThatHangsUpMidMessage_IsTheEndOfTheStream()
	{
		byte[] message = Tpkt(100, fill: 7);
		using MemoryStream wire = new(message[..50]);
		using RdpFrameReader reader = new(wire);

		await Assert.ThrowsAsync<EndOfStreamException>(async () => await reader.ReadPduAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task ReadPduAsync_ASilentServer_IsLeftWhenTheTokenSaysSo()
	{
		Pipe pipe = new();
		using RdpFrameReader reader = new(pipe.Reader.AsStream());
		using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		stop.CancelAfter(TimeSpan.FromMilliseconds(200));

		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.ReadPduAsync(stop.Token));
	}

	// A CredSSP message is DER, whose length is in a header of its own, and the server's answer to NTLM runs past the
	// head the reader looks at first.
	[Fact]
	public async Task ReadByHintAsync_ALongCredsspMessageInPieces_ComesOutWholeAndLeavesTheNext()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		byte[] message = new byte[4 + 3000];
		message[0] = 0x30;
		message[1] = 0x82;
		message[2] = 3000 >> 8;
		message[3] = 3000 & 0xFF;
		new Random(5).NextBytes(message.AsSpan(4));
		byte[] next = [0x30, 0x01, 0x00];
		Pipe pipe = new();
		using RdpFrameReader reader = new(pipe.Reader.AsStream());
		using CredsspHintSource source = new();

		ValueTask<byte[]> reading = reader.ReadByHintAsync(source.Hint, cancellationToken);
		for (int offset = 0; offset < message.Length; offset += 700)
		{
			await pipe.Writer.WriteAsync(message.AsMemory(offset, Math.Min(700, message.Length - offset)), cancellationToken);
		}

		await pipe.Writer.WriteAsync(next, cancellationToken);

		Assert.Equal(message, await reading.AsTask().WaitAsync(Wait, cancellationToken));
		Assert.Equal(next, await reader.ReadByHintAsync(source.Hint, cancellationToken).AsTask().WaitAsync(Wait, cancellationToken));
	}

	private static byte[] Tpkt(int length, byte fill)
	{
		byte[] message = new byte[length];
		Array.Fill(message, fill);
		message[0] = 0x03;
		message[1] = 0x00;
		message[2] = (byte)(length >> 8);
		message[3] = (byte)length;
		return message;
	}

	/// <summary>
	/// A connector walked to CredSSP over a fake X.224 answer, holding the hint for a CredSSP message. The hint is a view
	/// of the sequence, so everything stays alive until the test is done with it.
	/// </summary>
	private sealed class CredsspHintSource : IDisposable
	{
		private readonly Config _config;
		private readonly ClientConnector _connector;
		private readonly CredsspSequenceInitResult _init;
		private readonly CredsspSequence _sequence;

		public CredsspHintSource()
		{
			using ConfigBuilder builder = ConfigBuilder.New();
			builder.SetDesktopSize(600, 800);
			builder.SetClientName("t");
			builder.SetClientDir("C:\\");
			builder.SetEnableTls(true);
			builder.SetEnableCredssp(true);
			builder.WithUsernameAndPassword("alice", "pw");
			using PerformanceFlags flags = PerformanceFlags.NewDefault();
			builder.SetPerformanceFlags(flags);
			_config = builder.Build();
			_connector = ClientConnector.New(_config, "127.0.0.1:50000");
			using (WriteBuf buffer = WriteBuf.New())
			{
				// The X.224 request, then the server's answer choosing CredSSP.
				_connector.StepNoInput(buffer).Dispose();
				_connector.Step(FakeRdpServer.ConnectionConfirm(FakeRdpServer.ProtocolHybrid), buffer).Dispose();
			}

			_connector.MarkSecurityUpgradeAsDone();
			_init = CredsspSequence.Init(_connector, "host", new byte[270], null);
			_sequence = _init.GetCredsspSequence();
			Hint = _sequence.NextPduHint() ?? throw new InvalidOperationException("CredSSP asked for nothing.");
		}

		public PduHint Hint { get; }

		public void Dispose()
		{
			Hint.Dispose();
			_sequence.Dispose();
			_init.Dispose();
			_connector.Dispose();
			_config.Dispose();
		}
	}
}
