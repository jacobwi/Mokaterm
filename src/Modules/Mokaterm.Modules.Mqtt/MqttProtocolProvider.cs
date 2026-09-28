using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Mqtt.Protocol;
using Mokaterm.Modules.Mqtt.Sessions;
using MQTTnet;

namespace Mokaterm.Modules.Mqtt;

/// <summary>Opens MQTT sessions: the client, the TLS trust decision and the first subscriptions all run here.</summary>
internal sealed class MqttProtocolProvider : IProtocolProvider
{
	public const string ProtocolId = "mqtt";

	public const int DefaultPort = MqttConnectionOptions.PlainPort;

	/// <summary>
	/// Handshakes one connect may give up on because the certificate was still being decided. Two is enough for the
	/// refuse, ask, reconnect path; a third means something else keeps refusing.
	/// </summary>
	private const int MaxCertificateRounds = 3;

	private readonly ISettingsService _settings;
	private readonly TimeProvider _timeProvider;
	private readonly ILoggerFactory _loggerFactory;

	public MqttProtocolProvider(ISettingsService settings, TimeProvider timeProvider, ILoggerFactory loggerFactory)
	{
		_settings = settings;
		_timeProvider = timeProvider;
		_loggerFactory = loggerFactory;
	}

	public ProtocolDescriptor Descriptor { get; } = new()
	{
		Id = ProtocolId,
		DisplayName = "MQTT",
		Description = "A message broker: subscribe to topics, watch what arrives and publish. Plain, TLS or WebSockets.",
		DefaultPort = DefaultPort,
		Capabilities = ProtocolCapabilities.Messaging,

		// Many brokers take anyone, and the ones that do not use a user name and password in the CONNECT packet.
		AuthenticationMethods = [AuthenticationMethod.Password, AuthenticationMethod.Anonymous],
		RequiresUsername = false,
		Order = 50,
	};

	/// <summary>How long a TLS handshake waits for the host verifier. Tests shorten it.</summary>
	internal TimeSpan CertificateDecisionWait { get; init; } = MqttCertificateTrust.DefaultDecisionWait;

	public async Task<IProtocolSession> ConnectAsync(ProtocolConnectContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		MqttSettings settings = _settings.Get<MqttSettings>().Clamped();
		MqttConnectionOptions options = MqttConnectionOptions.From(context.Connection.Options);

		// An empty port means what these options imply: 8883 with TLS, 443 for secure WebSockets, and so on.
		int port = context.Connection.Port ?? options.DefaultPort;
		MqttEndpoint endpoint = MqttEndpoint.Create(context.Host.Address, port, options);

		EnsureSupported(context.Credentials.Method);
		MqttCertificateTrust? trust = options.UseTls
			? new MqttCertificateTrust(
				context.HostVerifier,
				endpoint.Host,
				endpoint.Port,
				_loggerFactory.CreateLogger<MqttCertificateTrust>(),
				CertificateDecisionWait)
			: null;

		try
		{
			return await ConnectCoreAsync(context, endpoint, options, settings, trust, cancellationToken);
		}
		catch
		{
			trust?.Dispose();
			throw;
		}
	}

	private static void EnsureSupported(AuthenticationMethod method)
	{
		if (method is AuthenticationMethod.PublicKey or AuthenticationMethod.Agent)
		{
			throw new ProtocolConnectException(
				ConnectFailure.AuthenticationFailed,
				"MQTT logs in with a user name and password in its CONNECT packet, or with nothing at all; key based logins are not part of the protocol.");
		}
	}

	/// <summary>
	/// Reads a login into a provider MQTTnet asks for its password while it writes each CONNECT. Null for an
	/// anonymous login, which sends no user name at all: that is what a broker with open access expects, and an
	/// empty one is not the same packet.
	/// </summary>
	private static MqttSecretCredentials? CreateCredentials(LoginCredentials? login) =>
		login is null || (login.Method == AuthenticationMethod.Anonymous && login.Password is null)
			? null

			// The buffer is copied: the caller disposes the login it was read from.
			: new MqttSecretCredentials(login.Username, login.Password?.Copy());

	/// <summary>True for the codes a different password could fix.</summary>
	private static bool IsLoginRefused(MqttClientConnectResultCode code) =>
		code is MqttClientConnectResultCode.BadUserNameOrPassword or MqttClientConnectResultCode.NotAuthorized;

	private async Task<IProtocolSession> ConnectCoreAsync(
		ProtocolConnectContext context,
		MqttEndpoint endpoint,
		MqttConnectionOptions options,
		MqttSettings settings,
		MqttCertificateTrust? trust,
		CancellationToken cancellationToken)
	{
		string clientId = MqttConnectionOptions.IsValidClientId(options.ClientId) ? options.ClientId : MqttClientIds.Create();
		LoginCredentials? login = null;
		MqttSecretCredentials? credentials = null;
		IMqttClient? client = null;
		try
		{
			if (context.Credentials.Method != AuthenticationMethod.Anonymous)
			{
				login = await context.Credentials.GetAsync(cancellationToken)
					?? throw new ProtocolConnectException(ConnectFailure.Cancelled, "The login was cancelled.");
			}

			for (int attempt = 1; ; attempt++)
			{
				credentials?.Dispose();
				credentials = CreateCredentials(login);
				client = new MqttClientFactory().CreateMqttClient();
				MqttClientConnectResult result = await ConnectClientAsync(
					client,
					MqttClientOptionsFactory.Create(endpoint, options, clientId, credentials, trust, settings.ConnectTimeout),
					endpoint,
					trust,
					context,
					cancellationToken);

				if (result.ResultCode == MqttClientConnectResultCode.Success)
				{
					// The session owns the client and the login provider from here, so neither is in the finally any more.
					IMqttClient connected = client;
					MqttSecretCredentials? connectedLogin = credentials;
					client = null;
					credentials = null;
					return await StartSessionAsync(context, endpoint, options, settings, clientId, result, connected, connectedLogin, trust, cancellationToken);
				}

				ProtocolConnectException failure = MqttConnectErrors.Describe(result, endpoint.Display);
				if (!IsLoginRefused(result.ResultCode) || attempt >= settings.AuthenticationAttempts)
				{
					throw failure;
				}

				// A broker refuses the whole CONNECT rather than asking again, so every retry is a new connection.
				client.Dispose();
				client = null;
				context.Status?.Report("Waiting for credentials");
				LoginCredentials retry = await context.Credentials.RetryAsync(failure.Message, cancellationToken) ?? throw failure;
				login?.Dispose();
				login = retry;
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex) when (ex is not ProtocolConnectException)
		{
			throw await MqttConnectErrors.DescribeAsync(ex, endpoint, trust?.RefusalCount ?? 0, CancellationToken.None);
		}
		finally
		{
			login?.Dispose();
			credentials?.Dispose();
			client?.Dispose();
		}
	}

	private async Task<IProtocolSession> StartSessionAsync(
		ProtocolConnectContext context,
		MqttEndpoint endpoint,
		MqttConnectionOptions options,
		MqttSettings settings,
		string clientId,
		MqttClientConnectResult result,
		IMqttClient client,
		MqttSecretCredentials? credentials,
		MqttCertificateTrust? trust,
		CancellationToken cancellationToken)
	{
		context.Status?.Report("Subscribing");
		MqttSession session = new(
			client,
			Describe(endpoint, options, clientId, result),
			new MqttMessageStore(settings.Caps(), _timeProvider),
			credentials,
			trust,
			_loggerFactory.CreateLogger<MqttSession>());

		try
		{
			await session.StartAsync(options.Subscriptions, cancellationToken);
			return session;
		}
		catch
		{
			// The session owns the client, the login and the trust now, so disposing it releases all three.
			await session.DisposeAsync();
			throw;
		}
	}

	/// <summary>
	/// Connects, and when a handshake failed because the user was still being asked about the certificate, waits for
	/// the answer and connects again with that certificate approved. The broker sees no CONNECT packet until TLS is
	/// up, so a refused handshake never shows it the login.
	/// </summary>
	private static async Task<MqttClientConnectResult> ConnectClientAsync(
		IMqttClient client,
		MqttClientOptions clientOptions,
		MqttEndpoint endpoint,
		MqttCertificateTrust? trust,
		ProtocolConnectContext context,
		CancellationToken cancellationToken)
	{
		for (int round = 1; ; round++)
		{
			int refusalsBefore = trust?.RefusalCount ?? 0;
			context.Status?.Report("Connecting");
			try
			{
				return await client.ConnectAsync(clientOptions, cancellationToken);
			}
			catch (Exception ex)
			{
				MqttCertificateDecision? refused = trust is not null && trust.RefusalCount != refusalsBefore ? trust.LastRefused : null;
				if (refused is null || cancellationToken.IsCancellationRequested)
				{
					throw await MqttConnectErrors.DescribeAsync(ex, endpoint, trust?.RefusalCount ?? 0, CancellationToken.None);
				}

				if (round >= MaxCertificateRounds)
				{
					throw CertificateRejected(endpoint);
				}

				if (!refused.IsDecided)
				{
					context.Status?.Report("Waiting for the certificate to be approved");
				}

				if (!await refused.WaitAsync(cancellationToken))
				{
					throw CertificateRejected(endpoint);
				}

				trust?.Approve(refused.Identity.Fingerprint);
			}
		}
	}

	private static ProtocolConnectException CertificateRejected(MqttEndpoint endpoint) =>
		new(ConnectFailure.HostIdentityRejected, $"The TLS certificate presented by {endpoint.Display} was not trusted.");

	private static MqttConnectionInfo Describe(
		MqttEndpoint endpoint,
		MqttConnectionOptions options,
		string clientId,
		MqttClientConnectResult result) => new()
		{
			ClientId = string.IsNullOrWhiteSpace(result.AssignedClientIdentifier) ? clientId : result.AssignedClientIdentifier,
			Endpoint = endpoint.Display,
			Transport = endpoint.Transport,
			IsEncrypted = endpoint.UseTls,
			Protocol = options.Protocol,
			SessionPresent = result.IsSessionPresent,
			ServerKeepAliveSeconds = result.ServerKeepAlive,
			RetainAvailable = result.RetainAvailable,
			MaximumQos = MqttWire.FromWire(result.MaximumQoS),
		};
}
