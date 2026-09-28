using System.Text;
using FluentFTP;
using FluentFTP.Proxy.AsyncProxy;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Ftp.Connection;
using Mokaterm.Modules.Ftp.Tests.Fakes;

namespace Mokaterm.Modules.Ftp.Tests;

public sealed class FtpProxyClientTests
{
	[Fact]
	public void Create_WithoutAProxy_MakesAPlainClient()
	{
		using LoginCredentials credentials = Login();

		using AsyncFtpClient client = Factory(ProxyOptions.None).Create(credentials);

		Assert.Equal(typeof(AsyncFtpClient), client.GetType());
		Assert.Equal("files.example.com", client.Host);
		Assert.Equal(21, client.Port);
	}

	[Theory]
	[InlineData(ProxyKind.Http, typeof(AsyncFtpClientHttp11Proxy))]
	[InlineData(ProxyKind.Socks4, typeof(AsyncFtpClientSocks4Proxy))]
	[InlineData(ProxyKind.Socks5, typeof(AsyncFtpClientSocks5Proxy))]
	public void Create_WithAProxy_MakesTheMatchingFluentFtpClient(ProxyKind kind, Type expected)
	{
		using LoginCredentials credentials = Login();

		using AsyncFtpClient client = Factory(Proxy(kind)).Create(credentials);

		Assert.Equal(expected, client.GetType());

		// The client still talks to the server; only the socket underneath it goes through the proxy.
		Assert.Equal("files.example.com", client.Host);
		Assert.Equal(21, client.Port);
		Assert.Equal("bob", client.Credentials.UserName);
		Assert.Equal("hunter2", client.Credentials.Password);
	}

	[Fact]
	public void Create_WithAProxy_KeepsTheConnectionsOwnConfigurationAndEncoding()
	{
		using LoginCredentials credentials = Login();

		using AsyncFtpClient client = Factory(Proxy(ProxyKind.Socks5), FtpEncryption.Explicit).Create(credentials);

		Assert.Equal(FtpEncryptionMode.Explicit, client.Config.EncryptionMode);
		Assert.Equal(FtpDataConnectionType.PASVEX, client.Config.DataConnectionType);
		Assert.Equal(FtpSelfConnectMode.Never, client.Config.SelfConnectMode);
		Assert.Same(Encoding.UTF8, client.Encoding);
	}

	[Fact]
	public void Create_AProxyThatAsksForALoginWithNothingSaved_StillBuildsAClient()
	{
		// The proxy password is asked for while connecting, so the factory may be called before one exists.
		using LoginCredentials credentials = Login(proxyPassword: null);

		using AsyncFtpClient client = Factory(Proxy(ProxyKind.Socks5)).Create(credentials);

		Assert.Equal(typeof(AsyncFtpClientSocks5Proxy), client.GetType());
		Assert.Equal("bob", client.Credentials.UserName);
	}

	[Fact]
	public void Create_AProxyWithoutAnAddress_IsNotDialledAround()
	{
		using LoginCredentials credentials = Login();
		ProxyOptions incomplete = new() { Kind = ProxyKind.Socks5 };

		Assert.Throws<ProtocolConnectException>(incomplete.EnsureUsable);

		// EnsureUsable refuses the connect before this point; a plain client is what the factory falls back to.
		using AsyncFtpClient client = Factory(incomplete).Create(credentials);
		Assert.Equal(typeof(AsyncFtpClient), client.GetType());
	}

	private static ProxyOptions Proxy(ProxyKind kind) => new() { Kind = kind, Host = "proxy.example.com", User = "proxy-bob" };

	private static LoginCredentials Login(string? proxyPassword = "proxy-secret") => new()
	{
		Username = "bob",
		Method = AuthenticationMethod.Password,
		Password = SecretBuffer.FromString("hunter2"),
		ProxyPassword = proxyPassword is null ? null : SecretBuffer.FromString(proxyPassword),
	};

	private static FtpClientFactory Factory(ProxyOptions proxy, FtpEncryption encryption = FtpEncryption.None)
	{
		FtpClientOptions options = FtpClientOptions.Create(
			"files.example.com",
			21,
			FtpConnectionOptions.Default with { Encryption = encryption, Proxy = proxy },
			new FtpSettings());

		FtpCertificateTrust trust = new(FakeHostVerifier.Accepting(), options.Host, options.Port, NullLogger.Instance);
		return new FtpClientFactory(options, trust, NullLogger.Instance);
	}
}
