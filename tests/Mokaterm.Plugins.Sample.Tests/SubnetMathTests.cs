using System.Numerics;
using Mokaterm.Plugins.Sample.Subnets;

namespace Mokaterm.Plugins.Sample.Tests;

/// <summary>The subnet calculator's arithmetic for both families, the edge prefixes and what it refuses.</summary>
public sealed class SubnetMathTests
{
	[Fact]
	public void AnIPv4Slash24_HasANetworkABroadcastAnd254Hosts()
	{
		SubnetInfo info = Calculate("192.168.1.10/24");

		Assert.False(info.IsIPv6);
		Assert.Equal("192.168.1.0/24", info.Cidr);
		Assert.Equal("255.255.255.0", info.Mask.ToString());
		Assert.Equal("0.0.0.255", info.Wildcard?.ToString());
		Assert.Equal("192.168.1.255", info.Broadcast?.ToString());
		Assert.Equal("192.168.1.1", info.FirstHost.ToString());
		Assert.Equal("192.168.1.254", info.LastHost.ToString());
		Assert.Equal(new BigInteger(256), info.AddressCount);
		Assert.Equal(new BigInteger(254), info.HostCount);
	}

	[Theory]
	[InlineData("10.20.30.40/8", "10.0.0.0", "10.255.255.255", "10.0.0.1", "10.255.255.254", 16777214)]
	[InlineData("172.16.5.4/12", "172.16.0.0", "172.31.255.255", "172.16.0.1", "172.31.255.254", 1048574)]
	[InlineData("203.0.113.9/30", "203.0.113.8", "203.0.113.11", "203.0.113.9", "203.0.113.10", 2)]
	[InlineData("0.0.0.0/0", "0.0.0.0", "255.255.255.255", "0.0.0.1", "255.255.255.254", 4294967294)]
	public void IPv4Prefixes(string text, string network, string broadcast, string first, string last, long hosts)
	{
		SubnetInfo info = Calculate(text);

		Assert.Equal(network, info.Network.ToString());
		Assert.Equal(broadcast, info.Broadcast?.ToString());
		Assert.Equal(first, info.FirstHost.ToString());
		Assert.Equal(last, info.LastHost.ToString());
		Assert.Equal(new BigInteger(hosts), info.HostCount);
	}

	[Fact]
	public void ASlash31_GivesBothAddressesToHosts_AndHasNoBroadcast()
	{
		SubnetInfo info = Calculate("198.51.100.7/31");

		Assert.Null(info.Broadcast);
		Assert.Equal("198.51.100.6", info.FirstHost.ToString());
		Assert.Equal("198.51.100.7", info.LastHost.ToString());
		Assert.Equal(new BigInteger(2), info.HostCount);
	}

	[Fact]
	public void AnAddressAlone_IsItsOwnHost()
	{
		SubnetInfo info = Calculate("198.51.100.7");

		Assert.Equal(32, info.PrefixLength);
		Assert.Null(info.Broadcast);
		Assert.Equal("198.51.100.7", info.FirstHost.ToString());
		Assert.Equal("198.51.100.7", info.LastHost.ToString());
		Assert.Equal(BigInteger.One, info.HostCount);
	}

	[Fact]
	public void ADottedMask_ReadsAsItsPrefix()
	{
		SubnetInfo info = Calculate("10.1.2.3/255.255.240.0");

		Assert.Equal(20, info.PrefixLength);
		Assert.Equal("10.1.0.0/20", info.Cidr);
	}

	[Fact]
	public void AnIPv6Slash64_HasNoBroadcast_AndEveryAddressIsAHosts()
	{
		SubnetInfo info = Calculate("2001:db8:abcd:12::5/64");

		Assert.True(info.IsIPv6);
		Assert.Equal("2001:db8:abcd:12::/64", info.Cidr);
		Assert.Equal("ffff:ffff:ffff:ffff::", info.Mask.ToString());
		Assert.Null(info.Wildcard);
		Assert.Null(info.Broadcast);
		Assert.Equal("2001:db8:abcd:12::", info.FirstHost.ToString());
		Assert.Equal("2001:db8:abcd:12:ffff:ffff:ffff:ffff", info.LastHost.ToString());
		Assert.Equal(BigInteger.One << 64, info.AddressCount);
		Assert.Equal(info.AddressCount, info.HostCount);
	}

	[Theory]
	[InlineData("2001:db8::1/128", "2001:db8::1", "2001:db8::1", 1)]
	[InlineData("2001:db8::1/127", "2001:db8::", "2001:db8::1", 2)]
	[InlineData("::/0", "::", "ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff", -1)]
	public void IPv6EdgePrefixes(string text, string first, string last, long count)
	{
		SubnetInfo info = Calculate(text);

		Assert.Equal(first, info.FirstHost.ToString());
		Assert.Equal(last, info.LastHost.ToString());
		Assert.Equal(count < 0 ? BigInteger.One << 128 : new BigInteger(count), info.AddressCount);
	}

	[Fact]
	public void AZone_IsNotPartOfThePrefix()
	{
		SubnetInfo info = Calculate("fe80::1%eth0/64");

		Assert.Equal("fe80::/64", info.Cidr);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("192.168.1.0/33")]
	[InlineData("2001:db8::/129")]
	[InlineData("10.1/8")]
	[InlineData("167772161/8")]
	[InlineData("192.168.1.0/255.0.255.0")]
	[InlineData("192.168.1.0/abc")]
	[InlineData("2001:db8::/255.255.0.0")]
	[InlineData("not an address")]
	public void WhatIsNoPrefix_IsRefusedWithAReason(string text)
	{
		Assert.False(SubnetMath.TryCalculate(text, out SubnetInfo? info, out string? problem));
		Assert.Null(info);
		Assert.False(string.IsNullOrWhiteSpace(problem));
	}

	private static SubnetInfo Calculate(string text)
	{
		Assert.True(SubnetMath.TryCalculate(text, out SubnetInfo? info, out string? problem), problem);
		return info;
	}
}
