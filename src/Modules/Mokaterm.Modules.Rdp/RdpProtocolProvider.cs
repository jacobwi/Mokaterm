using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Rdp.Protocol;
using Mokaterm.Modules.Rdp.Sessions;

namespace Mokaterm.Modules.Rdp;

/// <summary>Opens RDP sessions: the socket, TLS and the login check all run here, never in the page.</summary>
internal sealed class RdpProtocolProvider : IProtocolProvider
{
	public const string ProtocolId = "rdp";

	public const int DefaultPort = 3389;

	private readonly ISettingsService _settings;
	private readonly ILoggerFactory _loggerFactory;

	public RdpProtocolProvider(ISettingsService settings, ILoggerFactory loggerFactory)
	{
		_settings = settings;
		_loggerFactory = loggerFactory;
	}

	public ProtocolDescriptor Descriptor { get; } = new()
	{
		Id = ProtocolId,
		DisplayName = "RDP",
		Description = "Remote desktop over RDP, with TLS and network level authentication.",
		DefaultPort = DefaultPort,
		Capabilities = ProtocolCapabilities.RemoteDesktop,
		AuthenticationMethods = [AuthenticationMethod.Password, AuthenticationMethod.Anonymous],
		RequiresUsername = true,
		Order = 30,
	};

	public async Task<IProtocolSession> ConnectAsync(ProtocolConnectContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		RdpSettings settings = _settings.Get<RdpSettings>().Clamped();
		RdpConnectionOptions options = RdpConnectionOptions.From(context.Connection.Options);
		RdpConnector connector = new(
			context.Host.Address,
			context.Port,
			options,
			TimeSpan.FromSeconds(settings.ConnectTimeoutSeconds),
			context.HostVerifier);

		LoginCredentials? credentials = await GetCredentialsAsync(context.Credentials, cancellationToken);
		RdpTransport? transport = null;
		try
		{
			// A server that refuses the login closes the connection, so every retry dials again.
			for (int attempt = 1; transport is null; attempt++)
			{
				try
				{
					transport = await connector.ConnectAsync(credentials, context.Status, cancellationToken);
				}
				catch (RdpAuthenticationException ex)
				{
					if (attempt >= settings.AuthenticationAttempts)
					{
						throw new ProtocolConnectException(ConnectFailure.AuthenticationFailed, ex.Message, ex);
					}

					context.Status?.Report("Waiting for credentials");
					LoginCredentials retry = await context.Credentials.RetryAsync(ex.Message, cancellationToken)
						?? throw new ProtocolConnectException(ConnectFailure.AuthenticationFailed, ex.Message, ex);
					credentials?.Dispose();
					credentials = retry;
					context.Status?.Report("Connecting");
				}
			}

			// The session keeps its own copy: the one this method holds is disposed as soon as it returns.
			LoginCredentials? sessionCredentials = credentials?.Copy();
			try
			{
				RdpSession session = new(transport, settings, sessionCredentials, _loggerFactory.CreateLogger<RdpSession>());
				transport = null;
				return session;
			}
			catch
			{
				sessionCredentials?.Dispose();
				throw;
			}
		}
		finally
		{
			credentials?.Dispose();
			if (transport is not null)
			{
				await transport.DisposeAsync();
			}
		}
	}

	private static async ValueTask<LoginCredentials?> GetCredentialsAsync(ICredentialSource source, CancellationToken cancellationToken)
	{
		if (source.Method is AuthenticationMethod.PublicKey or AuthenticationMethod.Agent)
		{
			throw new ProtocolConnectException(
				ConnectFailure.AuthenticationFailed,
				"RDP logs in with a user name and a password; key based logins are not part of the protocol.");
		}

		if (source.Method == AuthenticationMethod.Anonymous)
		{
			// Nothing is sent, so the server shows its own login screen and asks there.
			return new LoginCredentials { Username = source.Username ?? "", Method = AuthenticationMethod.Anonymous };
		}

		return await source.GetAsync(cancellationToken)
			?? throw new ProtocolConnectException(ConnectFailure.Cancelled, "The login was cancelled.");
	}
}
