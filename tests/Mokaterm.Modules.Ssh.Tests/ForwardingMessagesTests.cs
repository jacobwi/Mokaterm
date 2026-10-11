using System.Text;
using Mokaterm.Modules.Ssh.Agent;
using Mokaterm.Modules.Ssh.Forwarding;
using Renci.SshNet.Messages.Connection;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>The bytes forwarding puts on the wire, checked against RFC 4254 and OpenSSH's PROTOCOL file rather than against SSH.NET.</summary>
public sealed class ForwardingMessagesTests
{
	[Fact]
	public void AgentRequest_IsTheNameAndNoReply()
	{
		ChannelRequestMessage message = new(42, new AgentForwardingRequest());

		SshWireReader wire = new(message.GetBytes());
		Assert.True(wire.TryReadByte(out byte number));
		Assert.Equal(98, number);
		Assert.True(wire.TryReadUInt32(out uint recipient));
		Assert.Equal(42u, recipient);
		Assert.True(wire.TryReadText(out string name));
		Assert.Equal("auth-agent-req@openssh.com", name);
		Assert.True(wire.TryReadByte(out byte wantReply));
		Assert.Equal(0, wantReply);
		Assert.Equal(0, wire.Remaining);
	}

	[Fact]
	public void X11Request_CarriesTheCookieAsHexAndTheScreen()
	{
		byte[] cookie = [.. Enumerable.Range(0xA0, 16).Select(value => (byte)value)];
		ChannelRequestMessage message = new(7, new X11ForwardingRequest(cookie, screen: 2));

		SshWireReader wire = new(message.GetBytes());
		Assert.True(wire.TryReadByte(out _));
		Assert.True(wire.TryReadUInt32(out uint recipient));
		Assert.Equal(7u, recipient);
		Assert.True(wire.TryReadText(out string name));
		Assert.Equal("x11-req", name);
		Assert.True(wire.TryReadByte(out byte wantReply));
		Assert.Equal(0, wantReply);
		Assert.True(wire.TryReadByte(out byte singleConnection));
		Assert.Equal(0, singleConnection);
		Assert.True(wire.TryReadText(out string protocol));
		Assert.Equal("MIT-MAGIC-COOKIE-1", protocol);
		Assert.True(wire.TryReadText(out string hex));
		Assert.Equal("a0a1a2a3a4a5a6a7a8a9aaabacadaeaf", hex);
		Assert.True(wire.TryReadUInt32(out uint screen));
		Assert.Equal(2u, screen);
		Assert.Equal(0, wire.Remaining);
	}

	[Fact]
	public void OpenConfirmation_PutsTheServersChannelFirstAndOursSecond()
	{
		// SSH.NET names the recipient "local" and the sender "remote"; the order on the wire is what matters.
		ChannelOpenConfirmationMessage message = new(9, 256 * 1024, 32 * 1024, 0xC000_0001);

		SshWireReader wire = new(message.GetBytes());
		Assert.True(wire.TryReadByte(out byte number));
		Assert.Equal(91, number);
		Assert.True(wire.TryReadUInt32(out uint recipient));
		Assert.True(wire.TryReadUInt32(out uint sender));
		Assert.True(wire.TryReadUInt32(out uint window));
		Assert.True(wire.TryReadUInt32(out uint packet));
		Assert.Equal((9u, 0xC000_0001u, 256u * 1024, 32u * 1024), (recipient, sender, window, packet));
	}

	[Fact]
	public void OpenConfirmation_ReadsTheServersChannelAsRemote()
	{
		// The shell's channel number comes from a confirmation the server sent, read through SSH.NET's own class.
		byte[] body = new SshWireWriter().WriteUInt32(0).WriteUInt32(5).WriteUInt32(2_097_152).WriteUInt32(32_768).ToArray();
		ChannelOpenConfirmationMessage message = new();

		message.Load(body);

		Assert.Equal(0u, message.LocalChannelNumber);
		Assert.Equal(5u, message.RemoteChannelNumber);
	}

	[Fact]
	public void OpenFailure_CarriesTheReason()
	{
		ChannelOpenFailureMessage message = new(9, "X11 connect failed", 2);

		SshWireReader wire = new(message.GetBytes());
		Assert.True(wire.TryReadByte(out byte number));
		Assert.Equal(92, number);
		Assert.True(wire.TryReadUInt32(out uint recipient));
		Assert.Equal(9u, recipient);
		Assert.True(wire.TryReadUInt32(out uint reason));
		Assert.Equal(2u, reason);
		Assert.True(wire.TryReadString(out byte[] description));
		Assert.Equal("X11 connect failed", Encoding.UTF8.GetString(description));
	}
}
