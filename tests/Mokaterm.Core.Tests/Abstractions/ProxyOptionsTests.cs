using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Core.Tests.Abstractions;

public sealed class ProxyOptionsTests
{
	private const string Prefix = "demo.proxy.";

	[Fact]
	public void From_EmptyOptions_IsOff()
	{
		ProxyOptions proxy = ProxyOptions.From(ProtocolOptions.Empty, Prefix);

		Assert.Equal(ProxyOptions.None, proxy);
		Assert.Equal(ProxyKind.None, proxy.Kind);
		Assert.False(proxy.IsEnabled);
		Assert.False(proxy.NeedsLogin);
	}

	[Fact]
	public void ApplyTo_ThenFrom_RoundTrips()
	{
		ProxyOptions original = new() { Kind = ProxyKind.Socks5, Host = "proxy.example.com", Port = 1081, User = "bob" };

		ProtocolOptions stored = original.ApplyTo(ProtocolOptions.Empty, Prefix);

		Assert.Equal("Socks5", stored["demo.proxy.kind"]);
		Assert.Equal("proxy.example.com", stored["demo.proxy.host"]);
		Assert.Equal("1081", stored["demo.proxy.port"]);
		Assert.Equal("bob", stored["demo.proxy.user"]);
		Assert.Equal(original, ProxyOptions.From(stored, Prefix));
	}

	[Fact]
	public void ApplyTo_UnsetValues_RemoveTheirKeys()
	{
		ProtocolOptions existing = new ProxyOptions { Kind = ProxyKind.Http, Host = "old", Port = 3128, User = "bob" }
			.ApplyTo(ProtocolOptions.Empty, Prefix);

		ProtocolOptions stored = new ProxyOptions { Kind = ProxyKind.Http, Host = "proxy" }.ApplyTo(existing, Prefix);

		Assert.Equal(["demo.proxy.host", "demo.proxy.kind"], stored.Keys.Order(StringComparer.Ordinal));
		Assert.Null(ProxyOptions.From(stored, Prefix).Port);
		Assert.Null(ProxyOptions.From(stored, Prefix).User);
	}

	[Fact]
	public void ApplyTo_TurnedOff_RemovesTheWholeGroupAndKeepsOtherKeys()
	{
		ProtocolOptions existing = new ProxyOptions { Kind = ProxyKind.Socks4, Host = "proxy", Port = 1080, User = "bob" }
			.ApplyTo(ProtocolOptions.Empty.With("other.module", "keep me"), Prefix);

		ProtocolOptions stored = ProxyOptions.None.ApplyTo(existing, Prefix);

		Assert.Equal(["other.module"], stored.Keys);
		Assert.Equal("keep me", stored["other.module"]);
	}

	[Fact]
	public void ApplyTo_BlankHostAndUser_RemoveTheirKeys()
	{
		ProtocolOptions stored = new ProxyOptions { Kind = ProxyKind.Http, Host = "  ", User = "  " }.ApplyTo(ProtocolOptions.Empty, Prefix);

		Assert.Equal(["demo.proxy.kind"], stored.Keys);
		Assert.False(ProxyOptions.From(stored, Prefix).IsEnabled);
	}

	[Fact]
	public void ApplyTo_TrimsHostAndUser()
	{
		ProtocolOptions stored = new ProxyOptions { Kind = ProxyKind.Socks5, Host = " proxy ", User = " bob " }.ApplyTo(ProtocolOptions.Empty, Prefix);

		Assert.Equal("proxy", stored["demo.proxy.host"]);
		Assert.Equal("bob", stored["demo.proxy.user"]);
	}

	[Theory]
	[InlineData("socks5", ProxyKind.Socks5)]
	[InlineData("HTTP", ProxyKind.Http)]
	[InlineData(" Socks4 ", ProxyKind.Socks4)]
	[InlineData("Sneakernet", ProxyKind.None)]
	[InlineData("9", ProxyKind.None)]
	[InlineData("2", ProxyKind.None)]

	// Enum.TryParse ORs a list, and Http | Socks4 is the value of Socks5: a stored value must name one kind.
	[InlineData("Http, Socks4", ProxyKind.None)]
	public void From_UnexpectedKinds_FallBackToNone(string stored, ProxyKind expected)
	{
		ProtocolOptions options = ProtocolOptions.Empty.With("demo.proxy.kind", stored).With("demo.proxy.host", "proxy");

		Assert.Equal(expected, ProxyOptions.From(options, Prefix).Kind);
	}

	[Fact]
	public void From_KindOff_DropsTheRestOfTheGroup()
	{
		ProtocolOptions options = ProtocolOptions.Empty
			.With("demo.proxy.host", "proxy")
			.With("demo.proxy.port", 1080)
			.With("demo.proxy.user", "bob");

		Assert.Equal(ProxyOptions.None, ProxyOptions.From(options, Prefix));
	}

	[Theory]
	[InlineData("0")]
	[InlineData("-5")]
	[InlineData("70000")]
	[InlineData("ten")]
	public void From_UnusablePort_ReadsBackAsTheDefault(string port)
	{
		ProtocolOptions options = new ProxyOptions { Kind = ProxyKind.Http, Host = "proxy" }
			.ApplyTo(ProtocolOptions.Empty, Prefix)
			.With("demo.proxy.port", port);

		ProxyOptions proxy = ProxyOptions.From(options, Prefix);

		Assert.Null(proxy.Port);
		Assert.Equal(ProxyOptions.DefaultHttpPort, proxy.EffectivePort);
	}

	[Fact]
	public void EffectivePort_FollowsTheKindWhenUnset()
	{
		Assert.Equal(8080, new ProxyOptions { Kind = ProxyKind.Http }.EffectivePort);
		Assert.Equal(1080, new ProxyOptions { Kind = ProxyKind.Socks4 }.EffectivePort);
		Assert.Equal(1080, new ProxyOptions { Kind = ProxyKind.Socks5 }.EffectivePort);
		Assert.Equal(3128, new ProxyOptions { Kind = ProxyKind.Http, Port = 3128 }.EffectivePort);
	}

	[Fact]
	public void NeedsLogin_OnlyWithAUser()
	{
		Assert.False(new ProxyOptions { Kind = ProxyKind.Socks5, Host = "proxy" }.NeedsLogin);
		Assert.False(new ProxyOptions { Kind = ProxyKind.Socks5, Host = "proxy", User = "   " }.NeedsLogin);
		Assert.True(new ProxyOptions { Kind = ProxyKind.Socks5, Host = "proxy", User = "bob" }.NeedsLogin);
		Assert.False((ProxyOptions.None with { User = "bob" }).NeedsLogin);
	}

	[Fact]
	public void Validate_OffIsAlwaysValid() => Assert.Null(ProxyOptions.None.Validate());

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("  ")]
	[InlineData("proxy with spaces")]
	[InlineData("user@proxy")]
	[InlineData("proxy/path")]
	[InlineData("proxy\\path")]
	[InlineData("proxy")]
	public void ValidateHost_RefusesWhatCannotBeDialled(string? host) => Assert.NotNull(ProxyOptions.ValidateHost(host));

	[Fact]
	public void ValidateHost_RefusesAHostLongerThanSocks5CanCarry()
	{
		Assert.Null(ProxyOptions.ValidateHost(new string('a', ProxyOptions.MaxHostLength)));
		Assert.NotNull(ProxyOptions.ValidateHost(new string('a', ProxyOptions.MaxHostLength + 1)));
	}

	[Theory]
	[InlineData(null)]
	[InlineData(1)]
	[InlineData(65535)]
	public void ValidatePort_AcceptsTheUsableRange(int? port) => Assert.Null(ProxyOptions.ValidatePort(port));

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(65536)]
	public void ValidatePort_RefusesTheRest(int port) => Assert.NotNull(ProxyOptions.ValidatePort(port));

	[Fact]
	public void ValidateUser_RefusesOnlyWhatAProxyCannotCarry()
	{
		Assert.Null(ProxyOptions.ValidateUser(null));
		Assert.Null(ProxyOptions.ValidateUser("bob"));
		Assert.NotNull(ProxyOptions.ValidateUser("bo\tb"));
		Assert.NotNull(ProxyOptions.ValidateUser(new string('a', ProxyOptions.MaxUserLength + 1)));
	}

	[Fact]
	public void EnsureUsable_AProxyWithoutAnAddress_RefusesTheConnectRatherThanDiallingAround()
	{
		ProxyOptions proxy = new() { Kind = ProxyKind.Socks5 };

		ProtocolConnectException error = Assert.Throws<ProtocolConnectException>(proxy.EnsureUsable);

		Assert.Equal(ConnectFailure.ProtocolError, error.Failure);
		Assert.Contains("proxy", error.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void EnsureUsable_AUsableProxyOrNone_DoesNothing()
	{
		ProxyOptions.None.EnsureUsable();
		new ProxyOptions { Kind = ProxyKind.Http, Host = "proxy", Port = 3128, User = "bob" }.EnsureUsable();
	}

	[Fact]
	public void Describe_NamesTheProxyAndThePortThatWillBeUsed()
	{
		Assert.Equal("proxy:8080", new ProxyOptions { Kind = ProxyKind.Http, Host = "proxy" }.Describe());
		Assert.Equal("proxy:3128", new ProxyOptions { Kind = ProxyKind.Http, Host = "proxy", Port = 3128 }.Describe());
	}

	[Fact]
	public void KeyHelpers_NameTheGroup()
	{
		Assert.Equal("demo.proxy.kind", ProxyOptions.KindKey(Prefix));
		Assert.Equal("demo.proxy.host", ProxyOptions.HostKey(Prefix));
		Assert.Equal("demo.proxy.port", ProxyOptions.PortKey(Prefix));
		Assert.Equal("demo.proxy.user", ProxyOptions.UserKey(Prefix));
	}
}
