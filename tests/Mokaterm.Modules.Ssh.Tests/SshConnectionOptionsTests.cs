using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Ssh.Tests;

public sealed class SshConnectionOptionsTests
{
	[Fact]
	public void RoundTrip_KeepsEveryValue()
	{
		SshConnectionOptions options = new()
		{
			KeepAliveSeconds = 0,
			StartupCommand = "tmux new -A -s main",
			TerminalType = "xterm-256color",
			InitialDirectory = "~/sites",
			Elevation = SshElevationMode.None,
		};

		SshConnectionOptions roundTripped = SshConnectionOptions.FromOptions(options.ApplyTo(ProtocolOptions.Empty));

		Assert.Equal(options, roundTripped);
	}

	[Fact]
	public void ApplyTo_WritesTheDocumentedKeys()
	{
		ProtocolOptions stored = new SshConnectionOptions
		{
			KeepAliveSeconds = 45,
			StartupCommand = "htop",
			TerminalType = "screen",
			InitialDirectory = "/var/log",
			Elevation = SshElevationMode.None,
		}.ApplyTo(ProtocolOptions.Empty);

		Assert.Equal("45", stored["ssh.keepAliveSeconds"]);
		Assert.Equal("htop", stored["ssh.startupCommand"]);
		Assert.Equal("screen", stored["ssh.terminalType"]);
		Assert.Equal("/var/log", stored["ssh.initialDirectory"]);
		Assert.Equal("None", stored["ssh.elevation"]);
	}

	[Fact]
	public void RoundTrip_KeepsTheProxy()
	{
		SshConnectionOptions options = new()
		{
			Proxy = new ProxyOptions { Kind = ProxyKind.Socks5, Host = "proxy.example.com", Port = 1081, User = "bob" },
		};

		ProtocolOptions stored = options.ApplyTo(ProtocolOptions.Empty);

		Assert.Equal("Socks5", stored["ssh.proxy.kind"]);
		Assert.Equal("proxy.example.com", stored["ssh.proxy.host"]);
		Assert.Equal("1081", stored["ssh.proxy.port"]);
		Assert.Equal("bob", stored["ssh.proxy.user"]);
		Assert.Equal(options, SshConnectionOptions.FromOptions(stored));
	}

	[Fact]
	public void ApplyTo_ProxyTurnedOff_RemovesTheWholeGroup()
	{
		ProtocolOptions existing = new SshConnectionOptions
		{
			Proxy = new ProxyOptions { Kind = ProxyKind.Http, Host = "proxy", Port = 3128, User = "bob" },
		}.ApplyTo(ProtocolOptions.Empty.With("ssh.startupCommand", "htop"));

		ProtocolOptions stored = new SshConnectionOptions { StartupCommand = "htop" }.ApplyTo(existing);

		Assert.Equal(["ssh.startupCommand"], stored.Keys);
		Assert.Equal(ProxyOptions.None, SshConnectionOptions.FromOptions(stored).Proxy);
	}

	[Fact]
	public void FromOptions_ProxyWithoutAKind_IsOff()
	{
		ProtocolOptions options = ProtocolOptions.Empty.With("ssh.proxy.host", "proxy").With("ssh.proxy.user", "bob");

		Assert.Equal(ProxyOptions.None, SshConnectionOptions.FromOptions(options).Proxy);
	}

	[Fact]
	public void ApplyTo_RemovesUnsetValuesAndKeepsOtherKeys()
	{
		ProtocolOptions existing = ProtocolOptions.Empty
			.With("ssh.startupCommand", "htop")
			.With("ssh.elevation", "None")
			.With("ssh.keepAliveSeconds", 10)
			.With("other.module", "keep me");

		ProtocolOptions updated = new SshConnectionOptions { StartupCommand = "  " }.ApplyTo(existing);

		Assert.Equal(["other.module"], updated.Keys);
		Assert.Equal("keep me", updated["other.module"]);
	}

	[Fact]
	public void FromOptions_Empty_UsesDefaults()
	{
		SshConnectionOptions options = SshConnectionOptions.FromOptions(ProtocolOptions.Empty);

		Assert.Equal(new SshConnectionOptions(), options);
		Assert.Equal(SshElevationMode.Auto, options.Elevation);
		Assert.Null(options.KeepAliveSeconds);
	}

	[Theory]
	[InlineData("ssh.keepAliveSeconds", "-5")]
	[InlineData("ssh.keepAliveSeconds", "ten")]
	[InlineData("ssh.elevation", "sometimes")]
	[InlineData("ssh.elevation", "7")]
	public void FromOptions_IgnoresValuesThatDoNotParse(string key, string value)
	{
		SshConnectionOptions options = SshConnectionOptions.FromOptions(ProtocolOptions.Empty.With(key, value));

		Assert.Null(options.KeepAliveSeconds);
		Assert.Equal(SshElevationMode.Auto, options.Elevation);
	}

	[Fact]
	public void FromOptions_ElevationIsCaseInsensitive() =>
		Assert.Equal(SshElevationMode.None, SshConnectionOptions.FromOptions(ProtocolOptions.Empty.With("ssh.elevation", "none")).Elevation);

	[Theory]
	[InlineData("xterm-256color", true)]
	[InlineData("vt100", true)]
	[InlineData("screen.xterm+new_1", true)]
	[InlineData("", false)]
	[InlineData("xterm 256", false)]
	[InlineData("xterm;rm -rf /", false)]
	[InlineData(null, false)]
	public void IsValidTerminalType_AcceptsTermNames(string? value, bool expected) =>
		Assert.Equal(expected, SshConnectionOptions.IsValidTerminalType(value));
}
