using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Modules.Vnc.Protocol;
using Mokaterm.Modules.Vnc.Sessions;
using Mokaterm.Modules.Vnc.Tests.Fakes;

namespace Mokaterm.Modules.Vnc.Tests;

/// <summary>The relay over a stream that is not a socket, where every read is observable.</summary>
public sealed class VncRelayUnitTests
{
	private static readonly byte[] ServerInit = [0, 64, 0, 48, 32, 24, 0, 1, 0, 255, 0, 255, 0, 255, 16, 8, 0, 0, 0, 0, 0, 0, 0, 2, (byte)'h', (byte)'i'];

	[Fact]
	public async Task Pump_StopsReadingWhileThePageIsBusy()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		FakeSessionStream stream = new();
		using RecordingSink sink = new();
		await using VncRelay relay = new(stream, ServerInit, sink, NullLogger.Instance);
		await relay.StartAsync(cancellationToken);
		await relay.SendAsync(PageHandshake(), cancellationToken);
		int handshakeBytes = sink.Bytes.Length;
		sink.Block();

		// Ten chunks are queued; the relay may only take the one it is trying to deliver.
		for (int i = 0; i < 10; i++)
		{
			stream.Push(new byte[VncRelay.ChunkSize]);
		}

		await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
		Assert.Equal(VncRelay.ChunkSize, stream.BytesRead);
		Assert.Equal(handshakeBytes, sink.Bytes.Length);

		sink.Release();
		await sink.WaitForBytesAsync(handshakeBytes + (10 * VncRelay.ChunkSize), cancellationToken);
		Assert.Equal(10 * VncRelay.ChunkSize, stream.BytesRead);
	}

	[Fact]
	public async Task Pump_StartsOnlyAfterThePageFinishedItsHandshake()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		FakeSessionStream stream = new();
		using RecordingSink sink = new();
		await using VncRelay relay = new(stream, ServerInit, sink, NullLogger.Instance);
		await relay.StartAsync(cancellationToken);
		stream.Push([2]);

		// Nothing is read while the page is still being told about the server.
		await relay.SendAsync("RFB 003.008\n"u8.ToArray(), cancellationToken);
		await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
		Assert.Equal(0, stream.BytesRead);

		await relay.SendAsync(new byte[] { 1, 1 }, cancellationToken);
		await sink.WaitForChunksAsync(5, cancellationToken);
		Assert.Equal(1, stream.BytesRead);
	}

	[Fact]
	public async Task Send_ForwardsOnlyWhatFollowsTheHandshake()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		FakeSessionStream stream = new();
		using RecordingSink sink = new();
		await using VncRelay relay = new(stream, ServerInit, sink, NullLogger.Instance);
		await relay.StartAsync(cancellationToken);

		await relay.SendAsync(PageHandshake(), cancellationToken);
		await relay.SendAsync(new byte[] { 5, 6, 7 }, cancellationToken);

		Assert.Equal(new byte[] { 5, 6, 7 }, stream.ToServer);
	}

	[Fact]
	public async Task Completion_EndsWhenTheServerClosesTheStream()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		FakeSessionStream stream = new();
		using RecordingSink sink = new();
		await using VncRelay relay = new(stream, ServerInit, sink, NullLogger.Instance);
		await relay.StartAsync(cancellationToken);
		await relay.SendAsync(PageHandshake(), cancellationToken);

		stream.End();

		await relay.Completion.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
		Assert.True(relay.Completion.IsCompletedSuccessfully);
	}

	[Fact]
	public async Task Send_AWrongHandshake_FaultsTheRelay()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		FakeSessionStream stream = new();
		using RecordingSink sink = new();
		await using VncRelay relay = new(stream, ServerInit, sink, NullLogger.Instance);
		await relay.StartAsync(cancellationToken);

		await Assert.ThrowsAsync<VncProtocolException>(async () =>
			await relay.SendAsync("RFB 003.003\n"u8.ToArray(), cancellationToken));

		IOException failure = await Assert.ThrowsAsync<IOException>(() => relay.Completion);
		Assert.IsType<VncProtocolException>(failure.InnerException);
	}

	private static byte[] PageHandshake() => [.. "RFB 003.008\n"u8, 1, 1];
}
