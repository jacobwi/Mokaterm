using Mokaterm.Modules.Vnc.Protocol;
using Mokaterm.Modules.Vnc.Sessions;

namespace Mokaterm.Modules.Vnc.Tests;

/// <summary>The handshake the page side is given, which stands in for the one .NET already ran with the server.</summary>
public sealed class VncPageHandshakeTests
{
	private static readonly byte[] ServerInit = [0, 64, 0, 48, 32, 24, 0, 1, 0, 255, 0, 255, 0, 255, 16, 8, 0, 0, 0, 0, 0, 0, 0, 2, (byte)'h', (byte)'i'];

	[Fact]
	public void Greeting_IsVersion38()
	{
		Assert.Equal("RFB 003.008\n"u8.ToArray(), VncPageHandshake.Greeting());
	}

	[Fact]
	public void Consume_AnswersEveryStepAndReplaysTheServerInit()
	{
		VncPageHandshake handshake = new(ServerInit);
		List<byte[]> replies = [];

		ReadOnlyMemory<byte> tail = handshake.Consume(VncHandshakeBytes(), replies);

		Assert.True(handshake.IsComplete);
		Assert.True(tail.IsEmpty);
		Assert.Equal(3, replies.Count);
		Assert.Equal(new byte[] { 1, 1 }, replies[0]);
		Assert.Equal(new byte[] { 0, 0, 0, 0 }, replies[1]);
		Assert.Equal(ServerInit, replies[2]);
	}

	[Fact]
	public void Consume_ByteByByte_AnswersTheSameWay()
	{
		VncPageHandshake handshake = new(ServerInit);
		List<byte[]> replies = [];

		foreach (byte value in VncHandshakeBytes())
		{
			Assert.True(handshake.Consume(new[] { value }, replies).IsEmpty);
		}

		Assert.True(handshake.IsComplete);
		Assert.Equal(3, replies.Count);
		Assert.Equal(ServerInit, replies[2]);
	}

	[Fact]
	public void Consume_KeepsWhatFollowsTheHandshakeForTheServer()
	{
		VncPageHandshake handshake = new(ServerInit);
		List<byte[]> replies = [];
		byte[] data = [.. VncHandshakeBytes(), 3, 0, 0, 0];

		ReadOnlyMemory<byte> tail = handshake.Consume(data, replies);

		Assert.Equal(new byte[] { 3, 0, 0, 0 }, tail.ToArray());
	}

	[Fact]
	public void Consume_AfterTheHandshake_ForwardsEverything()
	{
		VncPageHandshake handshake = new(ServerInit);
		List<byte[]> replies = [];
		handshake.Consume(VncHandshakeBytes(), replies);

		ReadOnlyMemory<byte> tail = handshake.Consume(new byte[] { 4, 1, 0, 0 }, replies);

		Assert.Equal(new byte[] { 4, 1, 0, 0 }, tail.ToArray());
		Assert.Equal(3, replies.Count);
	}

	[Fact]
	public void Consume_AnotherSecurityType_Throws()
	{
		VncPageHandshake handshake = new(ServerInit);
		List<byte[]> replies = [];
		byte[] data = [.. "RFB 003.008\n"u8, 2];

		Assert.Throws<VncProtocolException>(() => handshake.Consume(data, replies));
	}

	[Fact]
	public void Consume_AnotherVersion_Throws()
	{
		VncPageHandshake handshake = new(ServerInit);
		List<byte[]> replies = [];

		Assert.Throws<VncProtocolException>(() => handshake.Consume("RFB 003.003\n"u8.ToArray(), replies));
	}

	private static byte[] VncHandshakeBytes() => [.. "RFB 003.008\n"u8, 1, 1];
}
