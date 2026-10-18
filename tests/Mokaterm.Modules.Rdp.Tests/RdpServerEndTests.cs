using Mokaterm.Modules.Rdp.Protocol;
using Mokaterm.Modules.Rdp.Tests.Fakes;

namespace Mokaterm.Modules.Rdp.Tests;

/// <summary>Reading the two messages a server ends a session with, and nothing that only looks like them.</summary>
public sealed class RdpServerEndTests
{
	private const int IoChannel = FakeStage.DefaultIoChannel;

	[Theory]
	[InlineData(RdpErrorInfo.RpcInitiatedDisconnect)]
	[InlineData(RdpErrorInfo.RpcInitiatedLogoff)]
	[InlineData(RdpErrorInfo.DisconnectedByOtherConnection)]
	[InlineData(RdpErrorInfo.RpcInitiatedDisconnectByUser)]
	[InlineData(RdpErrorInfo.LogoffByUser)]
	public void Read_ASetErrorInfoOnTheSessionChannel_IsAnEndWithItsWords(uint code)
	{
		RdpServerEnd? end = RdpServerEnd.Read(RdpPdus.SetErrorInfo(code), IoChannel);

		Assert.NotNull(end);
		Assert.Equal(RdpErrorInfo.Describe(code), end.Reason);
		Assert.False(end.Reconnects);
		Assert.Equal("Set Error Info " + RdpErrorInfo.Hex(code), end.Code);
	}

	[Fact]
	public void Read_TheGraphicsSystemFailing_IsAnEndThatReconnects()
	{
		RdpServerEnd? end = RdpServerEnd.Read(RdpPdus.SetErrorInfo(RdpErrorInfo.GraphicsSubsystemFailed), IoChannel);

		Assert.NotNull(end);
		Assert.True(end.Reconnects);
	}

	[Fact]
	public void Read_ALengthWrittenInTwoBytes_IsReadAsWell() =>
		Assert.NotNull(RdpServerEnd.Read(RdpPdus.SetErrorInfo(RdpErrorInfo.LogoffByUser, longLength: true), IoChannel));

	[Fact]
	public void Read_AnErrorCodeOfZero_IsTheServerClearingIt() =>
		Assert.Null(RdpServerEnd.Read(RdpPdus.SetErrorInfo(RdpErrorInfo.None), IoChannel));

	[Fact]
	public void Read_ASetErrorInfoOnAnotherChannel_IsNotAnEnd() =>
		Assert.Null(RdpServerEnd.Read(RdpPdus.SetErrorInfo(RdpErrorInfo.LogoffByUser, channel: IoChannel + 1), IoChannel));

	[Fact]
	public void Read_ACompressedSetErrorInfo_IsLeftToIronRdp() =>
		Assert.Null(RdpServerEnd.Read(RdpPdus.SetErrorInfo(RdpErrorInfo.LogoffByUser, compressedType: 0x21), IoChannel));

	[Fact]
	public void Read_AnotherShareDataPdu_IsNotAnEnd() =>
		Assert.Null(RdpServerEnd.Read(RdpPdus.SaveSessionInfo(), IoChannel));

	[Fact]
	public void Read_AShareControlPduThatIsNotData_IsNotAnEnd()
	{
		// A Deactivate All (PDUTYPE_DEACTIVATEALLPDU) whose bytes happen to have 0x2F where a data PDU's type would be.
		byte[] body = [0x0C, 0, 0, 0];
		byte[] message = RdpPdus.SendDataIndication(IoChannel, RdpPdus.ShareData(0x2F, body, pduType: 0x16));

		Assert.Null(RdpServerEnd.Read(message, IoChannel));
	}

	[Fact]
	public void Read_TheUltimatumFromTheSpecification_IsAUserRequestedEnd()
	{
		// MS-RDPBCGR 2.2.2.3's example, rn-user-requested.
		byte[] message = [0x03, 0x00, 0x00, 0x09, 0x02, 0xF0, 0x80, 0x21, 0x80];

		RdpServerEnd? end = RdpServerEnd.Read(message, IoChannel);

		Assert.Equal(message, RdpPdus.Ultimatum(3));
		Assert.NotNull(end);
		Assert.Equal("The remote session was signed out or disconnected on the server.", end.Reason);
		Assert.False(end.Reconnects);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(4)]
	public void Read_AnUltimatumForAnyOtherReason_IsTheServerDisconnecting(int reason)
	{
		RdpServerEnd? end = RdpServerEnd.Read(RdpPdus.Ultimatum(reason), IoChannel);

		Assert.NotNull(end);
		Assert.Equal("The server disconnected the remote session.", end.Reason);
		Assert.False(end.Reconnects);
		Assert.EndsWith($"reason {reason}", end.Code, StringComparison.Ordinal);
	}

	[Fact]
	public void Read_AFastPathMessage_IsNotAnEnd() =>
		Assert.Null(RdpServerEnd.Read([0x00, 0x08, 0x21, 0x80, 0x00, 0x00, 0x00, 0x00], IoChannel));

	[Fact]
	public void Read_AMessageCutAnywhere_NeverThrows()
	{
		byte[] whole = RdpPdus.SetErrorInfo(RdpErrorInfo.LogoffByUser);
		for (int length = 0; length < whole.Length; length++)
		{
			Assert.Null(RdpServerEnd.Read(whole.AsSpan(0, length), IoChannel));
		}

		Assert.Null(RdpServerEnd.Read(RdpPdus.Ultimatum(3).AsSpan(0, 8), IoChannel));
	}

	[Fact]
	public void Read_ALengthThatClaimsMoreThanTheMessageHolds_IsNotAnEnd()
	{
		byte[] message = RdpPdus.SetErrorInfo(RdpErrorInfo.LogoffByUser);

		// The PER length of the user data sits right after the priority byte.
		message[7 + 6] = 0x7F;

		Assert.Null(RdpServerEnd.Read(message, IoChannel));
	}

	[Fact]
	public void Terminated_EndsCleanlyWithAGeneralReason()
	{
		Assert.False(RdpServerEnd.Terminated.Reconnects);
		Assert.Equal("The server ended the remote session.", RdpServerEnd.Terminated.Reason);
	}
}
