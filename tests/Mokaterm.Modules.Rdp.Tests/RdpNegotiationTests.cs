using Mokaterm.Modules.Rdp.Protocol;
using Mokaterm.Modules.Rdp.Tests.Fakes;

namespace Mokaterm.Modules.Rdp.Tests;

/// <summary>Reading the server's answer to the RDP request, and which picks a request accepts.</summary>
public sealed class RdpNegotiationTests
{
	private static readonly RdpSecurityProtocols TlsAlone = RdpNegotiation.Requested(credssp: false);

	private static readonly RdpSecurityProtocols WithCredssp = RdpNegotiation.Requested(credssp: true);

	[Fact]
	public void Requested_IsWhatIronRdpAsksFor()
	{
		// connection.rs: PROTOCOL_SSL with enable_tls, PROTOCOL_HYBRID | PROTOCOL_HYBRID_EX with enable_credssp.
		Assert.Equal(RdpSecurityProtocols.Ssl, TlsAlone);
		Assert.Equal(RdpSecurityProtocols.Ssl | RdpSecurityProtocols.Hybrid | RdpSecurityProtocols.HybridEx, WithCredssp);
	}

	[Theory]
	[InlineData(FakeRdpServer.ProtocolSsl)]
	[InlineData(FakeRdpServer.ProtocolHybrid)]
	[InlineData(8)]
	[InlineData(FakeRdpServer.ProtocolRdp)]
	public void Read_AResponse_GivesThePick(int wire)
	{
		RdpNegotiation? answer = RdpNegotiation.Read(FakeRdpServer.ConnectionConfirm(wire));

		Assert.Equal(new RdpNegotiation((RdpSecurityProtocols)wire, null), answer);
	}

	[Theory]
	[InlineData(RdpNegotiation.SslRequiredByServer)]
	[InlineData(RdpNegotiation.SslCertNotOnServer)]
	[InlineData(RdpNegotiation.HybridRequiredByServer)]
	[InlineData(0x42u)]
	public void Read_AFailure_GivesItsCode(uint code)
	{
		RdpNegotiation? answer = RdpNegotiation.Read(FakeRdpServer.NegotiationFailure((int)code));

		Assert.NotNull(answer);
		Assert.Equal(code, answer.FailureCode);
		Assert.False(answer.Picked(WithCredssp));
	}

	[Fact]
	public void Read_AnAnswerWithNoNegotiation_IsStandardRdpSecurity() =>
		Assert.Equal(new RdpNegotiation(RdpSecurityProtocols.Rdp, null), RdpNegotiation.Read(FakeRdpServer.ConfirmWithoutNegotiation()));

	[Fact]
	public void Read_SomethingThatIsNotAConfirm_IsLeftToIronRdp()
	{
		Assert.Null(RdpNegotiation.Read([0x03, 0x00, 0x00, 0x07, 0x02, 0x00, 0x00]));
		Assert.Null(RdpNegotiation.Read(RdpPdus.SaveSessionInfo()));

		// A negotiation of a type RDP does not define.
		byte[] odd = FakeRdpServer.ConnectionConfirm(FakeRdpServer.ProtocolSsl);
		odd[11] = 0x07;
		Assert.Null(RdpNegotiation.Read(odd));
	}

	[Fact]
	public void Read_AConfirmCutAnywhere_NeverThrows()
	{
		byte[] whole = FakeRdpServer.ConnectionConfirm(FakeRdpServer.ProtocolSsl);
		for (int length = 0; length < whole.Length; length++)
		{
			RdpNegotiation? answer = RdpNegotiation.Read(whole.AsSpan(0, length));
			Assert.True(answer is null || answer.Selected == RdpSecurityProtocols.Rdp, $"cut at {length}");
		}
	}

	// PROTOCOL_SSL 1, PROTOCOL_HYBRID 2, PROTOCOL_HYBRID_EX 8, PROTOCOL_RDSAAD 0x10, Standard RDP Security 0.
	[Theory]
	[InlineData(1u, false, true)]
	[InlineData(2u, false, false)]
	[InlineData(0u, false, false)]
	[InlineData(1u, true, true)]
	[InlineData(2u, true, true)]
	[InlineData(8u, true, true)]
	[InlineData(0u, true, false)]
	[InlineData(0x10u, true, false)]
	[InlineData(3u, true, false)]
	public void Picked_OnlyAcceptsExactlyOneProtocolThatWasAskedFor(uint selected, bool credssp, bool accepted) =>
		Assert.Equal(accepted, new RdpNegotiation((RdpSecurityProtocols)selected, null).Picked(RdpNegotiation.Requested(credssp)));

	[Fact]
	public void Names_AreTheSpecificationsOwn()
	{
		Assert.Equal("PROTOCOL_SSL", RdpNegotiation.Name(RdpSecurityProtocols.Ssl));
		Assert.Equal("PROTOCOL_HYBRID_EX", RdpNegotiation.Name(RdpSecurityProtocols.HybridEx));
		Assert.Equal("protocols 0x00000003", RdpNegotiation.Name(RdpSecurityProtocols.Ssl | RdpSecurityProtocols.Hybrid));
		Assert.Equal("HYBRID_REQUIRED_BY_SERVER", RdpNegotiation.FailureName(RdpNegotiation.HybridRequiredByServer));
		Assert.Equal("0x00000042", RdpNegotiation.FailureName(0x42));
	}
}
