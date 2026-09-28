using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Vnc.Protocol;
using Mokaterm.Modules.Vnc.Sessions;

namespace Mokaterm.Modules.Vnc;

/// <summary>Opens VNC sessions: the socket, the RFB handshake and the authentication all run here, never in the page.</summary>
internal sealed class VncProtocolProvider : IProtocolProvider
{
	public const string ProtocolId = "vnc";

	/// <summary>Display :0. Every further display adds one, so :1 is 5901.</summary>
	public const int DefaultPort = 5900;

	private readonly ISettingsService _settings;
	private readonly ILoggerFactory _loggerFactory;

	public VncProtocolProvider(ISettingsService settings, ILoggerFactory loggerFactory)
	{
		_settings = settings;
		_loggerFactory = loggerFactory;
	}

	public ProtocolDescriptor Descriptor { get; } = new()
	{
		Id = ProtocolId,
		DisplayName = "VNC",
		Description = "Remote desktop over VNC (RFB), with TLS when the server offers VeNCrypt.",
		DefaultPort = DefaultPort,
		Capabilities = ProtocolCapabilities.RemoteDesktop,
		AuthenticationMethods = [AuthenticationMethod.Password, AuthenticationMethod.Anonymous],
		RequiresUsername = false,
		Order = 20,
	};

	public async Task<IProtocolSession> ConnectAsync(ProtocolConnectContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		VncSettings settings = _settings.Get<VncSettings>().Clamped();
		VncConnectionOptions options = VncConnectionOptions.From(context.Connection.Options);
		VncConnector connector = new(
			context.Host.Address,
			context.Port,
			options,
			TimeSpan.FromSeconds(settings.ConnectTimeoutSeconds),
			context.HostVerifier);

		LoginCredentials? credentials = await GetCredentialsAsync(context.Credentials, cancellationToken);
		VncTransport? transport = null;
		try
		{
			// A VNC server closes the connection on a refused password, so every retry dials again.
			for (int attempt = 1; transport is null; attempt++)
			{
				try
				{
					transport = await connector.ConnectAsync(credentials, context.Status, cancellationToken);
				}
				catch (VncAuthenticationException ex)
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

			// The session keeps its own copy so it can dial again after its view was unmounted.
			LoginCredentials? sessionCredentials = credentials?.Copy();
			try
			{
				VncSession session = new(connector, transport, sessionCredentials, _loggerFactory.CreateLogger<VncSession>());
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
				"VNC logs in with a password or without credentials; key based logins are not part of the protocol.");
		}

		if (source.Method == AuthenticationMethod.Anonymous)
		{
			// Servers that need no password never ask for one, and this login has none to give.
			return new LoginCredentials { Username = source.Username ?? "", Method = AuthenticationMethod.Anonymous };
		}

		return await source.GetAsync(cancellationToken)
			?? throw new ProtocolConnectException(ConnectFailure.Cancelled, "The login was cancelled.");
	}
}
