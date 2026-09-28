using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Core.Tests.Abstractions;

/// <summary>
/// A session keeps copies of its login for companion connections and retries, so every copy has to carry every secret:
/// one that is dropped silently turns into a prompt, or a proxy that refuses the connection.
/// </summary>
public sealed class LoginCredentialsTests
{
	[Fact]
	public void Copy_CarriesEverySecretInBuffersOfItsOwn()
	{
		using LoginCredentials original = Full();

		using LoginCredentials copy = original.Copy();

		Assert.Equal("bob", copy.Username);
		Assert.Equal(AuthenticationMethod.PublicKey, copy.Method);
		Assert.Equal("hunter2", copy.Password?.RevealString());
		Assert.Equal("KEY", copy.PrivateKey?.RevealString());
		Assert.Equal("correct horse", copy.Passphrase?.RevealString());
		Assert.Equal("proxy-secret", copy.ProxyPassword?.RevealString());
		Assert.NotSame(original.ProxyPassword, copy.ProxyPassword);
	}

	[Fact]
	public void Copy_LeavesUnsetSecretsUnset()
	{
		using LoginCredentials original = new() { Username = "bob", Method = AuthenticationMethod.Anonymous };

		using LoginCredentials copy = original.Copy();

		Assert.Null(copy.Password);
		Assert.Null(copy.PrivateKey);
		Assert.Null(copy.Passphrase);
		Assert.Null(copy.ProxyPassword);
	}

	[Fact]
	public void WithProxyPassword_ReplacesOnlyTheProxyPassword()
	{
		using LoginCredentials original = Full();
		using SecretBuffer typed = SecretBuffer.FromString("typed-proxy-secret");

		using LoginCredentials result = original.WithProxyPassword(typed);

		Assert.Equal("typed-proxy-secret", result.ProxyPassword?.RevealString());
		Assert.Equal("hunter2", result.Password?.RevealString());
		Assert.Equal("KEY", result.PrivateKey?.RevealString());
		Assert.Equal("correct horse", result.Passphrase?.RevealString());

		// Neither the buffer passed in nor the credentials it came from are taken over.
		Assert.NotSame(typed, result.ProxyPassword);
		Assert.Equal("typed-proxy-secret", typed.RevealString());
		Assert.Equal("proxy-secret", original.ProxyPassword?.RevealString());
	}

	[Fact]
	public void WithProxyPassword_Null_ClearsIt()
	{
		using LoginCredentials original = Full();

		using LoginCredentials result = original.WithProxyPassword(null);

		Assert.Null(result.ProxyPassword);
		Assert.Equal("hunter2", result.Password?.RevealString());
	}

	[Fact]
	public void Dispose_WipesEverySecret()
	{
		LoginCredentials credentials = Full();
		SecretBuffer? proxyPassword = credentials.ProxyPassword;

		credentials.Dispose();

		Assert.True(credentials.Password?.IsDisposed);
		Assert.True(credentials.PrivateKey?.IsDisposed);
		Assert.True(credentials.Passphrase?.IsDisposed);
		Assert.True(proxyPassword?.IsDisposed);
	}

	private static LoginCredentials Full() => new()
	{
		Username = "bob",
		Method = AuthenticationMethod.PublicKey,
		Password = SecretBuffer.FromString("hunter2"),
		PrivateKey = SecretBuffer.FromString("KEY"),
		Passphrase = SecretBuffer.FromString("correct horse"),
		ProxyPassword = SecretBuffer.FromString("proxy-secret"),
	};
}
