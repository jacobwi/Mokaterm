using System.Diagnostics;
using System.Text;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Vnc.Tests.Fakes;

namespace Mokaterm.Modules.Vnc.Tests.Loopback;

/// <summary>The relay between a page and a server connection: the synthetic handshake, both directions and backpressure.</summary>
public sealed class VncRelayTests
{
	private const int Megabyte = 1024 * 1024;

	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task Attach_GivesThePageTheSyntheticHandshakeAndTheRealServerInit()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackVncServer server = VncHarness.StartServer();
		(IProtocolSession session, IVncConnection connection) = await ConnectAsync(server, cancellationToken);
		await using (session)
		{
			LoopbackVncConnection accepted = await server.AcceptedAsync(cancellationToken);
			using RecordingSink sink = new();
			await using IVncChannel channel = await connection.AttachAsync(sink, cancellationToken);
			await channel.StartAsync(cancellationToken);

			await sink.WaitForChunksAsync(1, cancellationToken);
			Assert.Equal("RFB 003.008\n"u8.ToArray(), sink.Chunks.First());

			await channel.SendAsync("RFB 003.008\n"u8.ToArray(), cancellationToken);
			await channel.SendAsync(new byte[] { 1 }, cancellationToken);
			await channel.SendAsync(new byte[] { 1 }, cancellationToken);

			byte[][] chunks = [.. sink.Chunks];
			Assert.Equal(4, chunks.Length);

			// One security type: None.
			Assert.Equal(new byte[] { 1, 1 }, chunks[1]);

			// SecurityResult OK.
			Assert.Equal(new byte[] { 0, 0, 0, 0 }, chunks[2]);
			Assert.Equal(accepted.ServerInit, chunks[3]);
		}
	}

	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task Relay_MovesBytesInBothDirections()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackVncServer server = VncHarness.StartServer();
		(IProtocolSession session, IVncConnection connection) = await ConnectAsync(server, cancellationToken);
		await using (session)
		{
			LoopbackVncConnection accepted = await server.AcceptedAsync(cancellationToken);
			using RecordingSink sink = new();
			await using IVncChannel channel = await connection.AttachAsync(sink, cancellationToken);
			await CompleteHandshakeAsync(channel, sink, cancellationToken);

			// A SetPixelFormat message, which is what noVNC sends first once it has the ServerInit.
			byte[] setPixelFormat = [0, 0, 0, 0, 32, 24, 0, 1, 0, 255, 0, 255, 0, 255, 16, 8, 0, 0, 0, 0];
			await channel.SendAsync(setPixelFormat, cancellationToken);
			Assert.Equal(setPixelFormat, await accepted.ReadExactlyAsync(setPixelFormat.Length, cancellationToken));

			// The four handshake chunks are already recorded, so the session bytes start after the ServerInit.
			int handshakeLength = 12 + 2 + 4 + accepted.ServerInit.Length;
			await accepted.SendCutTextAsync("from the server", cancellationToken);
			await sink.WaitForBytesAsync(handshakeLength + 8 + 15, cancellationToken);

			byte[] relayed = sink.Bytes;
			Assert.Equal(3, relayed[handshakeLength]);
			Assert.Equal("from the server", Encoding.Latin1.GetString(relayed.AsSpan(handshakeLength + 8)));
		}
	}

	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task Relay_HoldsTheServerBackWhileThePageIsBusy()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackVncServer server = VncHarness.StartServer();
		(IProtocolSession session, IVncConnection connection) = await ConnectAsync(server, cancellationToken);
		await using (session)
		{
			LoopbackVncConnection accepted = await server.AcceptedAsync(cancellationToken);
			using RecordingSink sink = new();
			await using IVncChannel channel = await connection.AttachAsync(sink, cancellationToken);
			await CompleteHandshakeAsync(channel, sink, cancellationToken);

			int handshakeBytes = sink.Bytes.Length;
			sink.Block();
			byte[] payload = new byte[4 * Megabyte];
			Task sending = accepted.SendAsync(payload, cancellationToken);

			// The relay waits for the page to take the chunk it read and delivers nothing meanwhile, which is what
			// stops the server: its bytes stay in the socket buffers.
			await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
			Assert.Equal(handshakeBytes, sink.Bytes.Length);

			sink.Release();
			await sending;
			await sink.WaitForBytesAsync(handshakeBytes + payload.Length, cancellationToken);
			Assert.Equal(handshakeBytes + payload.Length, sink.Bytes.Length);
		}
	}

	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task Attach_AfterDetaching_OpensANewConnectionForTheNewPage()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackVncServer server = VncHarness.StartServer();
		(IProtocolSession session, IVncConnection connection) = await ConnectAsync(server, cancellationToken);
		await using (session)
		{
			LoopbackVncConnection first = await server.AcceptedAsync(cancellationToken);
			using RecordingSink firstSink = new();
			IVncChannel firstChannel = await connection.AttachAsync(firstSink, cancellationToken);
			await CompleteHandshakeAsync(firstChannel, firstSink, cancellationToken);
			await firstChannel.DisposeAsync();

			// Detaching took the page's protocol state with it, so the session dials again.
			Assert.Equal(0, await first.Stream.ReadAsync(new byte[1], cancellationToken));

			using RecordingSink secondSink = new();
			await using IVncChannel secondChannel = await connection.AttachAsync(secondSink, cancellationToken);
			LoopbackVncConnection second = await server.AcceptedAsync(cancellationToken);
			await CompleteHandshakeAsync(secondChannel, secondSink, cancellationToken);

			Assert.Equal(2, server.Attempts);
			Assert.Equal(second.ServerInit, secondSink.Chunks.Last());
		}
	}

	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task ServerClose_EndsTheSession()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackVncServer server = VncHarness.StartServer();
		(IProtocolSession session, IVncConnection connection) = await ConnectAsync(server, cancellationToken);
		await using (session)
		{
			LoopbackVncConnection accepted = await server.AcceptedAsync(cancellationToken);
			using RecordingSink sink = new();
			await using IVncChannel channel = await connection.AttachAsync(sink, cancellationToken);
			await CompleteHandshakeAsync(channel, sink, cancellationToken);

			await accepted.DisposeAsync();

			await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
			Assert.True(session.Completion.IsCompletedSuccessfully);
		}
	}

	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task Detach_AfterTheSessionClosed_DoesNothing()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackVncServer server = VncHarness.StartServer();
		(IProtocolSession session, IVncConnection connection) = await ConnectAsync(server, cancellationToken);
		using RecordingSink sink = new();
		IVncChannel channel = await connection.AttachAsync(sink, cancellationToken);
		await CompleteHandshakeAsync(channel, sink, cancellationToken);

		// The usual order when a session ends: the session manager closes the session, then the view detaches.
		await session.DisposeAsync();
		await channel.DisposeAsync();

		Assert.True(session.Completion.IsCompleted);
	}

	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task Send_BeforeTheHandshakeIsDone_NeverReachesTheServer()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackVncServer server = VncHarness.StartServer();
		(IProtocolSession session, IVncConnection connection) = await ConnectAsync(server, cancellationToken);
		await using (session)
		{
			LoopbackVncConnection accepted = await server.AcceptedAsync(cancellationToken);
			using RecordingSink sink = new();
			await using IVncChannel channel = await connection.AttachAsync(sink, cancellationToken);
			await channel.StartAsync(cancellationToken);

			// The page's whole handshake plus its first real message in one write, the way a page can batch them.
			byte[] burst = [.. VncHarness.PageHandshake(), 2, 0, 0, 0];
			await channel.SendAsync(burst, cancellationToken);

			// Only the message after the handshake is forwarded.
			Assert.Equal(new byte[] { 2, 0, 0, 0 }, await accepted.ReadExactlyAsync(4, cancellationToken));
			Assert.Equal(4, sink.Chunks.Count);
		}
	}

	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task Dispose_WhileANewViewIsDialing_StopsTheDialInsteadOfWaitingForIt()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;

		// The first connection works; the one a returning view dials is accepted and then never greeted.
		await using LoopbackVncServer server = VncHarness.StartServer(new LoopbackVncServerOptions { SilentFromAttempt = 2 });
		FakeSettingsService settings = new();
		settings.Set(new VncSettings { ConnectTimeoutSeconds = 45 });
		(IProtocolSession session, IVncConnection connection) = await VncHarness.ConnectAsync(
			VncHarness.CreateProvider(settings),
			VncHarness.CreateContext(server.Port, new FakeCredentialSource(null, null, AuthenticationMethod.Anonymous)),
			cancellationToken);

		using RecordingSink firstSink = new();
		IVncChannel first = await connection.AttachAsync(firstSink, cancellationToken);
		await CompleteHandshakeAsync(first, firstSink, cancellationToken);
		await first.DisposeAsync();

		using RecordingSink secondSink = new();
		Task<IVncChannel> dialing = connection.AttachAsync(secondSink, cancellationToken).AsTask();
		await Eventually.TrueAsync(() => server.Attempts == 2, "the second attach dials the server again");

		Stopwatch closing = Stopwatch.StartNew();
		await session.DisposeAsync();
		closing.Stop();

		Assert.True(closing.Elapsed < TimeSpan.FromSeconds(10), $"Closing the session waited {closing.Elapsed} for a dial nobody needs.");
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dialing);
		Assert.True(session.Completion.IsCompleted);
	}

	[Fact(Timeout = VncHarness.TestTimeoutMilliseconds)]
	public async Task Attach_AfterTheSessionClosed_DialsNothing()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackVncServer server = VncHarness.StartServer();
		(IProtocolSession session, IVncConnection connection) = await ConnectAsync(server, cancellationToken);
		await session.DisposeAsync();

		using RecordingSink sink = new();
		await Assert.ThrowsAsync<ObjectDisposedException>(() => connection.AttachAsync(sink, cancellationToken).AsTask());

		Assert.Equal(1, server.Attempts);
	}

	private static async Task<(IProtocolSession Session, IVncConnection Connection)> ConnectAsync(
		LoopbackVncServer server,
		CancellationToken cancellationToken) =>
		await VncHarness.ConnectAsync(
			VncHarness.CreateProvider(),
			VncHarness.CreateContext(server.Port, new FakeCredentialSource(null, null, AuthenticationMethod.Anonymous)),
			cancellationToken);

	private static async Task CompleteHandshakeAsync(IVncChannel channel, RecordingSink sink, CancellationToken cancellationToken)
	{
		await channel.StartAsync(cancellationToken);
		await sink.WaitForChunksAsync(1, cancellationToken);
		await channel.SendAsync(VncHarness.PageHandshake(), cancellationToken);
		await sink.WaitForChunksAsync(4, cancellationToken);
	}
}
