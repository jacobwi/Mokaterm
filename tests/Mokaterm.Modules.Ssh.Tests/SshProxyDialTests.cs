using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Ssh.Connection;
using Renci.SshNet;

namespace Mokaterm.Modules.Ssh.Tests;

public sealed class SshProxyDialTests
{
	private static readonly ProxyOptions Socks5 = new() { Kind = ProxyKind.Socks5, Host = "proxy.example.com", Port = 1081, User = "bob" };

	private static readonly SshEndpoint Target = new("10.0.0.5", 2222);

	private static AuthenticationMethod[] Methods() => [new NoneAuthenticationMethod("root")];

	[Theory]
	[InlineData(ProxyKind.None, ProxyTypes.None)]
	[InlineData(ProxyKind.Http, ProxyTypes.Http)]
	[InlineData(ProxyKind.Socks4, ProxyTypes.Socks4)]
	[InlineData(ProxyKind.Socks5, ProxyTypes.Socks5)]
	public void ToProxyType_MapsEveryKind(ProxyKind kind, ProxyTypes expected) =>
		Assert.Equal(expected, SshProxyDial.ToProxyType(kind));

	[Fact]
	public void ForDial_ADirectDialUsesTheConnectionsProxy() =>
		Assert.Equal(Socks5, SshProxyDial.ForDial(Socks5, dialsDirectly: true));

	[Fact]
	public void ForDial_BehindAJumpHostThereIsNoProxy() =>
		Assert.Equal(ProxyOptions.None, SshProxyDial.ForDial(Socks5, dialsDirectly: false));

	[Fact]
	public void CreateConnectionInfo_WithoutAProxy_DialsTheAddressItself()
	{
		ConnectionInfo info = SshProxyDial.CreateConnectionInfo(Target, "root", ProxyOptions.None, proxyPassword: null, Methods());

		Assert.Equal("10.0.0.5", info.Host);
		Assert.Equal(2222, info.Port);
		Assert.Equal(ProxyTypes.None, info.ProxyType);
	}

	[Fact]
	public void CreateConnectionInfo_WithAProxy_KeepsTheTargetAndNamesTheProxy()
	{
		using SecretBuffer proxyPassword = SecretBuffer.FromString("proxy-secret");

		ConnectionInfo info = SshProxyDial.CreateConnectionInfo(Target, "root", Socks5, proxyPassword, Methods());

		// The host stays the server: SSH.NET asks the proxy to reach it and exchanges keys with the server itself.
		Assert.Equal("10.0.0.5", info.Host);
		Assert.Equal(2222, info.Port);
		Assert.Equal(ProxyTypes.Socks5, info.ProxyType);
		Assert.Equal("proxy.example.com", info.ProxyHost);
		Assert.Equal(1081, info.ProxyPort);
		Assert.Equal("bob", info.ProxyUsername);
		Assert.Equal("proxy-secret", info.ProxyPassword);
	}

	[Fact]
	public void CreateConnectionInfo_AProxyWithoutALogin_SendsEmptyStringsRatherThanNull()
	{
		ProxyOptions anonymous = new() { Kind = ProxyKind.Socks4, Host = "proxy" };

		ConnectionInfo info = SshProxyDial.CreateConnectionInfo(Target, "root", anonymous, proxyPassword: null, Methods());

		Assert.Equal(ProxyTypes.Socks4, info.ProxyType);
		Assert.Equal(ProxyOptions.DefaultSocksPort, info.ProxyPort);
		Assert.Equal("", info.ProxyUsername);
		Assert.Equal("", info.ProxyPassword);
	}

	[Fact]
	public void CreateConnectionInfo_AKindWithoutAnAddress_DoesNotQuietlyDialAround()
	{
		ProxyOptions incomplete = new() { Kind = ProxyKind.Http };

		Assert.Throws<ProtocolConnectException>(incomplete.EnsureUsable);

		// EnsureUsable is what refuses it; the builder treats settings it cannot dial as no proxy at all.
		Assert.Equal(ProxyTypes.None, SshProxyDial.CreateConnectionInfo(Target, "root", incomplete, proxyPassword: null, Methods()).ProxyType);
	}
}
