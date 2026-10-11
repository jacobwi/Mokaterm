using System.Net;
using Mokaterm.Modules.Ssh.Forwarding;

namespace Mokaterm.Modules.Ssh.Tests;

public sealed class X11DisplayTests
{
	[Theory]
	[InlineData(null, "localhost", 0, 0)]
	[InlineData("  ", "localhost", 0, 0)]
	[InlineData("localhost:0.0", "localhost", 0, 0)]
	[InlineData(":0", "localhost", 0, 0)]
	[InlineData(":1.2", "localhost", 1, 2)]
	[InlineData(" 127.0.0.1:10 ", "127.0.0.1", 10, 0)]
	[InlineData("workstation.lan:3.1", "workstation.lan", 3, 1)]
	[InlineData("[::1]:4", "::1", 4, 0)]
	[InlineData("[fe80::1]:0.0", "fe80::1", 0, 0)]
	[InlineData("localhost:59535", "localhost", 59535, 0)]
	public void TryParse_ReadsDisplays(string? text, string host, int number, int screen)
	{
		Assert.True(X11Display.TryParse(text, out X11Display display, out string? error));

		Assert.Null(error);
		Assert.Equal(new X11Display(host, number, screen), display);
	}

	[Theory]
	[InlineData("localhost")]
	[InlineData("localhost:")]
	[InlineData("localhost:x")]
	[InlineData("localhost:-1")]
	[InlineData("localhost:59536")]
	[InlineData("localhost:0.256")]
	[InlineData("localhost:0.")]
	[InlineData("unix:0")]
	[InlineData("/tmp/.X11-unix/X0:0")]
	[InlineData("::1:0")]
	[InlineData("[127.0.0.1]:0")]
	[InlineData("[::1]0")]
	[InlineData("bad host:0")]
	[InlineData("-host:0")]
	public void TryParse_RefusesWhatIsNotATcpDisplay(string text)
	{
		Assert.False(X11Display.TryParse(text, out _, out string? error));

		Assert.False(string.IsNullOrWhiteSpace(error));
	}

	[Fact]
	public void Default_IsTcpPort6000OnIpv4Loopback()
	{
		X11Display display = X11Display.Default;

		Assert.Equal("localhost:0.0", display.ToString());
		Assert.Equal(X11Display.DefaultText, display.ToString());
		Assert.Equal(new IPEndPoint(IPAddress.Loopback, 6000), display.EndPoint);
		Assert.Equal("127.0.0.1:6000", display.DescribeEndPoint());
		Assert.True(display.IsLocal);
	}

	[Fact]
	public void EndPoint_FollowsTheHost()
	{
		Assert.True(X11Display.TryParse("[::1]:2", out X11Display v6, out _));
		Assert.True(X11Display.TryParse("10.0.0.5:1", out X11Display v4, out _));
		Assert.True(X11Display.TryParse("xserver.lan:0", out X11Display named, out _));

		Assert.Equal(new IPEndPoint(IPAddress.IPv6Loopback, 6002), v6.EndPoint);
		Assert.Equal("[::1]:2.0", v6.ToString());
		Assert.True(v6.IsLocal);
		Assert.Equal(new IPEndPoint(IPAddress.Parse("10.0.0.5"), 6001), v4.EndPoint);
		Assert.False(v4.IsLocal);
		Assert.Equal(new DnsEndPoint("xserver.lan", 6000), named.EndPoint);
		Assert.Equal("xserver.lan:6000", named.DescribeEndPoint());
	}
}
