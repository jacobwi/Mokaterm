using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Core.Tests.Abstractions;

/// <summary>
/// A retry after the server rejected a login must not ask for the proxy password again: the proxy already let that
/// connection through, and SSH and FTP both used to carry it over with their own copy of this.
/// </summary>
public sealed class ProxyLoginTests
{
	[Fact]
	public void CarryProxyPassword_MovesItOntoTheRetriedLogin()
	{
		using LoginCredentials used = WithProxyPassword("proxy-secret");
		LoginCredentials retry = new() { Username = "bob", Method = AuthenticationMethod.Password, Password = SecretBuffer.FromString("second try") };

		using LoginCredentials carried = ProxyLogin.CarryProxyPassword(retry, used);

		Assert.NotSame(retry, carried);
		Assert.Equal("proxy-secret", carried.ProxyPassword?.RevealString());
		Assert.Equal("second try", carried.Password?.RevealString());

		// The replaced instance is disposed, so the retry's own buffers are not left behind.
		Assert.True(retry.Password?.IsDisposed);
		Assert.Equal("proxy-secret", used.ProxyPassword?.RevealString());
	}

	[Fact]
	public void CarryProxyPassword_WithNothingToCarry_HandsBackTheSameLogin()
	{
		using LoginCredentials used = new() { Username = "bob", Method = AuthenticationMethod.Password };
		using LoginCredentials retry = new() { Username = "bob", Method = AuthenticationMethod.Password, Password = SecretBuffer.FromString("second try") };

		Assert.Same(retry, ProxyLogin.CarryProxyPassword(retry, used));
		Assert.Equal("second try", retry.Password?.RevealString());
	}

	private static LoginCredentials WithProxyPassword(string secret)
	{
		using SecretBuffer buffer = SecretBuffer.FromString(secret);
		return new LoginCredentials
		{
			Username = "bob",
			Method = AuthenticationMethod.Password,
			Password = SecretBuffer.FromString("hunter2"),
			ProxyPassword = buffer.Copy(),
		};
	}
}
