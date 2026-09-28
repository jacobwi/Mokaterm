using Mokaterm.Abstractions.Connections;

namespace Mokaterm.Core.Tests.Abstractions;

public sealed class QuickConnectTargetTests
{
	[Theory]
	[InlineData("host", null, null, "host", null)]
	[InlineData("user@host", null, "user", "host", null)]
	[InlineData("user@host:2222", null, "user", "host", 2222)]
	[InlineData("ssh://user@host:22", "ssh", "user", "host", 22)]
	[InlineData("ftp://host", "ftp", null, "host", null)]
	[InlineData("SFTP://files.example.com/", "sftp", null, "files.example.com", null)]
	[InlineData("  admin@10.0.0.5:2200  ", null, "admin", "10.0.0.5", 2200)]
	[InlineData("me@corp@host", null, "me@corp", "host", null)]
	public void TryParse_HostForms(string text, string? protocol, string? username, string address, int? port) =>
		AssertParsed(text, protocol, username, address, port);

	[Theory]
	[InlineData("root@[::1]:22", null, "root", "::1", 22)]
	[InlineData("[fe80::1%eth0]", null, null, "fe80::1%eth0", null)]
	[InlineData("fe80::1", null, null, "fe80::1", null)]
	[InlineData("ssh://[2001:db8::10]:2022", "ssh", null, "2001:db8::10", 2022)]
	[InlineData("deploy@2001:db8::10", null, "deploy", "2001:db8::10", null)]
	public void TryParse_Ipv6Forms(string text, string? protocol, string? username, string address, int? port) =>
		AssertParsed(text, protocol, username, address, port);

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("@host")]
	[InlineData("user@")]
	[InlineData("host:0")]
	[InlineData("host:65536")]
	[InlineData("host:abc")]
	[InlineData("host:")]
	[InlineData("host:+22")]
	[InlineData("[::1")]
	[InlineData("[]")]
	[InlineData("[::1]x")]
	[InlineData("a b")]
	[InlineData("host/path")]
	[InlineData("bad scheme!://host")]
	[InlineData("://host")]
	[InlineData("ssh://")]
	public void TryParse_InvalidInput_ReturnsFalse(string? text)
	{
		Assert.False(QuickConnectTarget.TryParse(text, out QuickConnectTarget? target));
		Assert.Null(target);
	}

	private static void AssertParsed(string text, string? protocol, string? username, string address, int? port)
	{
		Assert.True(QuickConnectTarget.TryParse(text, out QuickConnectTarget? target));
		Assert.Equal(new QuickConnectTarget(protocol, username, address, port), target);
	}
}
