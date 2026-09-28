using System.Globalization;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Core.Tests.Abstractions;

/// <summary>
/// Every failure message and every label names a server through this, so an IPv6 address that loses its brackets reads
/// as a different address: <c>::1:5900</c> says nothing about where the port starts.
/// </summary>
public sealed class HostEndpointTests
{
	[Theory]
	[InlineData("web01", 22, "web01:22")]
	[InlineData("10.10.2.3", 2222, "10.10.2.3:2222")]
	[InlineData("::1", 5900, "[::1]:5900")]
	[InlineData("fe80::1%eth0", 22, "[fe80::1%eth0]:22")]
	[InlineData("2001:db8::8a2e:370:7334", 3389, "[2001:db8::8a2e:370:7334]:3389")]
	public void Format_BracketsIpv6(string host, int port, string expected) =>
		Assert.Equal(expected, HostEndpoint.Format(host, port));

	[Fact]
	public void Format_AnAddressThatAlreadyHasBrackets_IsLeftAsItIs() =>
		Assert.Equal("[::1]:5900", HostEndpoint.Format("[::1]", 5900));

	[Fact]
	public void Format_WritesThePortTheSameInEveryCulture()
	{
		CultureInfo original = CultureInfo.CurrentCulture;
		try
		{
			// A culture with its own digits would otherwise write a port nothing can dial.
			CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
			Assert.Equal("web01:2222", HostEndpoint.Format("web01", 2222));
		}
		finally
		{
			CultureInfo.CurrentCulture = original;
		}
	}
}
