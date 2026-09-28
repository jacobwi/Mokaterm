using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Sessions.Tests.Fakes;

namespace Mokaterm.Sessions.Tests;

public sealed class ProxyLoginTests
{
	private static readonly ProxyOptions WithLogin = new() { Kind = ProxyKind.Socks5, Host = "proxy.example.com", Port = 1081, User = "proxy-bob" };

	private readonly FakeUserInteraction _interaction = new();

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task EnsurePasswordAsync_ProxyWithoutALogin_AsksNothing()
	{
		using LoginCredentials credentials = Login();

		LoginCredentials result = await ProxyLogin.EnsurePasswordAsync(credentials, WithLogin with { User = null }, _interaction, Ct);

		Assert.Same(credentials, result);
		Assert.Empty(_interaction.SecretPrompts);
	}

	[Fact]
	public async Task EnsurePasswordAsync_NoProxy_AsksNothing()
	{
		using LoginCredentials credentials = Login();

		LoginCredentials result = await ProxyLogin.EnsurePasswordAsync(credentials, ProxyOptions.None, _interaction, Ct);

		Assert.Same(credentials, result);
		Assert.Empty(_interaction.SecretPrompts);
	}

	[Fact]
	public async Task EnsurePasswordAsync_PasswordAlreadySaved_AsksNothing()
	{
		using LoginCredentials credentials = Login(proxyPassword: "saved-proxy-password");

		LoginCredentials result = await ProxyLogin.EnsurePasswordAsync(credentials, WithLogin, _interaction, Ct);

		Assert.Same(credentials, result);
		Assert.Empty(_interaction.SecretPrompts);
		Assert.Equal("saved-proxy-password", result.ProxyPassword?.RevealString());
	}

	[Fact]
	public async Task EnsurePasswordAsync_NothingSaved_AsksAndKeepsTheRestOfTheLogin()
	{
		LoginCredentials credentials = Login();
		_interaction.AnswerSecret("typed-proxy-password");

		using LoginCredentials result = await ProxyLogin.EnsurePasswordAsync(credentials, WithLogin, _interaction, Ct);

		SecretPrompt prompt = Assert.Single(_interaction.SecretPrompts);
		Assert.Contains("proxy.example.com:1081", prompt.Message, StringComparison.Ordinal);
		Assert.Contains("proxy-bob", prompt.Message, StringComparison.Ordinal);
		Assert.NotSame(credentials, result);
		Assert.Equal("typed-proxy-password", result.ProxyPassword?.RevealString());
		Assert.Equal("bob", result.Username);
		Assert.Equal("hunter2", result.Password?.RevealString());

		// The instance it replaced is disposed, so no copy of the login is left behind.
		Assert.True(credentials.Password?.IsDisposed);
	}

	[Fact]
	public async Task EnsurePasswordAsync_CancelledPrompt_FailsTheConnect()
	{
		using LoginCredentials credentials = Login();

		ProtocolConnectException error = await Assert.ThrowsAsync<ProtocolConnectException>(
			async () => await ProxyLogin.EnsurePasswordAsync(credentials, WithLogin, _interaction, Ct));

		Assert.Equal(ConnectFailure.Cancelled, error.Failure);
	}

	private static LoginCredentials Login(string? proxyPassword = null) => new()
	{
		Username = "bob",
		Method = AuthenticationMethod.Password,
		Password = SecretBuffer.FromString("hunter2"),
		ProxyPassword = proxyPassword is null ? null : SecretBuffer.FromString(proxyPassword),
	};
}
