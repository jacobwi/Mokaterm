using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Abstractions.Protocols;

/// <summary>How every module gets the password of the proxy it dials through.</summary>
public static class ProxyLogin
{
	/// <summary>
	/// Asks for the proxy password when the proxy wants a login and the credentials carry none, and returns credentials
	/// that hold the answer, so retries and a session's companion connections reuse it instead of asking again. Returns
	/// the credentials it was given when nothing has to be asked; a new instance replaces and disposes that one.
	/// </summary>
	/// <exception cref="ProtocolConnectException">The prompt was cancelled.</exception>
	public static async Task<LoginCredentials> EnsurePasswordAsync(
		LoginCredentials credentials,
		ProxyOptions proxy,
		IUserInteraction interaction,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(credentials);
		ArgumentNullException.ThrowIfNull(proxy);
		ArgumentNullException.ThrowIfNull(interaction);
		if (!proxy.NeedsLogin || credentials.ProxyPassword is not null)
		{
			return credentials;
		}

		SecretPromptResult answer = await interaction.PromptSecretAsync(
			new SecretPrompt
			{
				Title = "Proxy password",
				Message = $"The proxy {proxy.Describe()} asks {proxy.User} for a password.",
				Label = "Password",
			},
			cancellationToken) ?? throw new ProtocolConnectException(ConnectFailure.Cancelled, "The login was cancelled.");

		using SecretBuffer typed = SecretBuffer.FromString(answer.Secret);
		LoginCredentials withProxyPassword = credentials.WithProxyPassword(typed);
		credentials.Dispose();
		return withProxyPassword;
	}

	/// <summary>
	/// <paramref name="retry"/> carrying the proxy password <paramref name="used"/> already got through: the proxy let
	/// this connection through and only the server's own login is being retried, so the user is not asked for it again.
	/// Returns <paramref name="retry"/> itself when there is nothing to carry, and disposes it when it does not.
	/// </summary>
	public static LoginCredentials CarryProxyPassword(LoginCredentials retry, LoginCredentials used)
	{
		ArgumentNullException.ThrowIfNull(retry);
		ArgumentNullException.ThrowIfNull(used);
		if (used.ProxyPassword is not { } proxyPassword)
		{
			return retry;
		}

		LoginCredentials carried = retry.WithProxyPassword(proxyPassword);
		retry.Dispose();
		return carried;
	}
}
