using Mokaterm.Modules.Rdp.Protocol;

namespace Mokaterm.Modules.Rdp.Tests;

public sealed class RdpCredsspTraceTests
{
	private static readonly byte[] KerberosOid = [0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x12, 0x01, 0x02, 0x02];

	[Fact]
	public void Mechanism_NtlmTokenAfterAKerberosMechList_IsNtlm() =>
		Assert.Equal("NTLM", RdpCredsspTrace.Mechanism([0x30, .. KerberosOid, .. "NTLMSSP\0"u8, 0x01]));

	[Fact]
	public void Mechanism_KerberosAlone_IsKerberos() =>
		Assert.Equal("Kerberos", RdpCredsspTrace.Mechanism([0x30, .. KerberosOid, 0x6E]));

	[Fact]
	public void Mechanism_NeitherSignature_IsUnnamed() =>
		Assert.Null(RdpCredsspTrace.Mechanism([0x30, 0x82, 0x01, 0x02, 0xA3]));

	[Fact]
	public void Sent_NamesTheMechanismAndTheSize() =>
		Assert.Equal("Login: sent message 1 (NTLM, 8 bytes)", RdpCredsspTrace.Sent(1, "NTLMSSP\0"u8));

	[Fact]
	public void Sent_WithoutAMechanism_GivesTheSizeOnly() =>
		Assert.Equal("Login: sent message 3 (2 bytes)", RdpCredsspTrace.Sent(3, [0x30, 0x00]));
}
