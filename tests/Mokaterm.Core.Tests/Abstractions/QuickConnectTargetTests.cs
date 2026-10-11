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

	[Theory]
	[InlineData("ssh://user@host", "ssh")]
	[InlineData("  RDP://", "rdp")]
	[InlineData("serial:///dev/ttyUSB0", "serial")]
	[InlineData("kubernetes://", "kubernetes")]
	public void ReadScheme_NamesTheProtocolBeforeTheRestIsTyped(string text, string protocol) =>
		Assert.Equal(protocol, QuickConnectTarget.ReadScheme(text));

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("user@host")]
	[InlineData("ssh:/")]
	[InlineData("://host")]
	[InlineData("bad scheme!://host")]
	public void ReadScheme_WithoutAUsableScheme_IsNull(string? text) => Assert.Null(QuickConnectTarget.ReadScheme(text));

	// A serial port, a shell or a container is named, not dialled: slashes, colons and @ all belong to the name.
	[Theory]
	[InlineData("COM3", null, "COM3")]
	[InlineData("/dev/ttyUSB0", null, "/dev/ttyUSB0")]
	[InlineData("serial:///dev/ttyUSB0", "serial", "/dev/ttyUSB0")]
	[InlineData("serial://COM12", "serial", "COM12")]
	[InlineData(@"\\.\COM12", null, @"\\.\COM12")]
	[InlineData("  Ubuntu-22.04  ", null, "Ubuntu-22.04")]
	[InlineData("local://wsl", "local", "wsl")]
	[InlineData("docker://web_1", "docker", "web_1")]
	[InlineData("/dev/serial/by-id/usb-FTDI:1", null, "/dev/serial/by-id/usb-FTDI:1")]
	public void TryParseName_TakesTheWholeTextAsTheAddress(string text, string? protocol, string address)
	{
		Assert.True(QuickConnectTarget.TryParseName(text, out QuickConnectTarget? target));
		Assert.Equal(new QuickConnectTarget(protocol, null, address, null), target);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("serial://")]
	[InlineData("COM 3")]
	[InlineData("pwsh\t-NoLogo")]
	[InlineData("COM\a3")]
	[InlineData("bad scheme!://COM3")]
	public void TryParseName_RefusesWhatNoNameHolds(string? text)
	{
		Assert.False(QuickConnectTarget.TryParseName(text, out QuickConnectTarget? target));
		Assert.Null(target);
	}

	[Fact]
	public void TryParseName_RefusesANamePastTheCap()
	{
		Assert.True(QuickConnectTarget.TryParseName(new string('a', QuickConnectTarget.MaxNameLength), out _));
		Assert.False(QuickConnectTarget.TryParseName(new string('a', QuickConnectTarget.MaxNameLength + 1), out _));
	}

	// The host form refuses the same text, which is what used to keep /dev/ttyUSB0 out of quick connect.
	[Fact]
	public void TryParse_StillRefusesAPathAsAHost() => Assert.False(QuickConnectTarget.TryParse("/dev/ttyUSB0", out _));

	private static void AssertParsed(string text, string? protocol, string? username, string address, int? port)
	{
		Assert.True(QuickConnectTarget.TryParse(text, out QuickConnectTarget? target));
		Assert.Equal(new QuickConnectTarget(protocol, username, address, port), target);
	}
}
