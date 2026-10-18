using Mokaterm.Modules.Rdp.Protocol;

namespace Mokaterm.Modules.Rdp.Tests;

public sealed class RdpErrorInfoTests
{
	public static TheoryData<uint> Named => new(
		RdpErrorInfo.RpcInitiatedDisconnect,
		RdpErrorInfo.RpcInitiatedLogoff,
		RdpErrorInfo.IdleTimeout,
		RdpErrorInfo.LogonTimeout,
		RdpErrorInfo.DisconnectedByOtherConnection,
		RdpErrorInfo.OutOfMemory,
		RdpErrorInfo.ServerDeniedConnection,
		RdpErrorInfo.ServerInsufficientPrivileges,
		RdpErrorInfo.ServerFreshCredentialsRequired,
		RdpErrorInfo.RpcInitiatedDisconnectByUser,
		RdpErrorInfo.LogoffByUser,
		RdpErrorInfo.CloseStackOnDriverNotReady,
		RdpErrorInfo.ServerDwmCrash,
		RdpErrorInfo.CloseStackOnDriverFailure,
		RdpErrorInfo.CloseStackOnDriverIfaceFailure,
		RdpErrorInfo.ServerWinlogonCrash,
		RdpErrorInfo.ServerCsrssCrash,
		RdpErrorInfo.LicenseNoLicenseServer,
		RdpErrorInfo.LicenseNoLicense,
		RdpErrorInfo.LicenseNoRemoteConnections,
		RdpErrorInfo.GraphicsSubsystemFailed);

	[Theory]
	[MemberData(nameof(Named))]
	public void Describe_ANamedCode_IsAPlainSentenceWithoutTheCode(uint code)
	{
		string text = RdpErrorInfo.Describe(code);

		Assert.EndsWith(".", text, StringComparison.Ordinal);
		Assert.DoesNotContain("0x", text, StringComparison.Ordinal);
		Assert.DoesNotContain((char)0x2014, text);
		Assert.DoesNotContain((char)0x2013, text);
	}

	[Theory]
	[InlineData(0x0000_0103u, "licensing")]
	[InlineData(0x0000_0409u, "connection broker")]
	[InlineData(0x0000_10D0u, "protocol error")]
	[InlineData(0x0000_7777u, "ended the remote session")]
	public void Describe_TheRestOfARange_NamesTheRangeAndTheCode(uint code, string words)
	{
		string text = RdpErrorInfo.Describe(code);

		Assert.Contains(words, text, StringComparison.Ordinal);
		Assert.Contains(RdpErrorInfo.Hex(code), text, StringComparison.Ordinal);
	}

	[Fact]
	public void Hex_WritesTheCodeTheWayTheDocumentationDoes() => Assert.Equal("0x0000000C", RdpErrorInfo.Hex(RdpErrorInfo.LogoffByUser));

	[Theory]
	[MemberData(nameof(Named))]
	public void Reconnects_OnlyAfterTheGraphicsSystemFailed(uint code) =>
		Assert.Equal(code == RdpErrorInfo.GraphicsSubsystemFailed, RdpErrorInfo.Reconnects(code));
}
