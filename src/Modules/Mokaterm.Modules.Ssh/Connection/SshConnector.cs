using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Ssh.Agent;
using Mokaterm.Modules.Ssh.Keys;
using Renci.SshNet;
using Renci.SshNet.Common;
using LoginMethod = Mokaterm.Abstractions.Connections.AuthenticationMethod;

namespace Mokaterm.Modules.Ssh.Connection;

/// <summary>A connected, authenticated client and what its session keeps.</summary>
internal sealed record SshConnection<TClient>(TClient Client, SshSessionContext Context)
	where TClient : BaseClient;

/// <summary>
/// Connects and authenticates SSH.NET clients for both protocols: credentials and retries, key passphrases, agent keys,
/// jump hosts, host key verification with a reconnect after the user decides, timeouts, cancellation and error mapping.
/// </summary>
internal sealed class SshConnector
{
	private const int MaxPassphrasePrompts = 2;

	private readonly ISettingsService _settings;
	private readonly ILogger<SshConnector> _logger;

	public SshConnector(ISettingsService settings, ILoggerFactory? loggerFactory = null)
	{
		_settings = settings;
		_logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<SshConnector>();
	}

	public SshSettings Settings => _settings.Get<SshSettings>();

	public ISettingsService SettingsService => _settings;

	/// <summary>Connects for a new session. The returned context owns the credentials and any jump hosts.</summary>
	/// <exception cref="ProtocolConnectException">Connecting or logging in failed.</exception>
	public Task<SshConnection<TClient>> ConnectAsync<TClient>(ProtocolConnectContext context, Func<ConnectionInfo, TClient> createClient, CancellationToken cancellationToken)
		where TClient : BaseClient =>
		ConnectAsync(context, createClient, dial: null, trail: null, cancellationToken);

	/// <summary>
	/// Connects one hop of a jump chain. <paramref name="dial"/> is the loopback port of the hop before it, and
	/// replaces the address the socket goes to while the host key still belongs to the real machine.
	/// </summary>
	/// <exception cref="ProtocolConnectException">Connecting or logging in failed.</exception>
	public async Task<SshConnection<TClient>> ConnectAsync<TClient>(
		ProtocolConnectContext context,
		Func<ConnectionInfo, TClient> createClient,
		SshEndpoint? dial,
		SshJumpTrail? trail,
		CancellationToken cancellationToken)
		where TClient : BaseClient
	{
		ArgumentNullException.ThrowIfNull(context);
		try
		{
			return await ConnectCoreAsync(context, createClient, dial, trail, cancellationToken);
		}
		catch (ProtocolConnectException ex) when (PortGreeting.MayBeWrongProtocol(ex.Failure) && DialsDirectly(context, dial))
		{
			// A port that belongs to another protocol fails like a broken SSH server; its greeting says what it is.
			throw await PortGreeting.ExplainAsync(ex, context.Host.Address, context.Port, cancellationToken);
		}
	}

	/// <summary>
	/// Opens a second connection for a live session with its saved login. Only the host key the session trusted is
	/// accepted, and nothing is asked except questions a server puts to every login, such as a one-time code.
	/// </summary>
	/// <exception cref="ProtocolConnectException">Connecting or logging in failed.</exception>
	public async Task<TClient> ConnectCompanionAsync<TClient>(SshSessionContext context, Func<ConnectionInfo, TClient> createClient, CancellationToken cancellationToken)
		where TClient : BaseClient
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(createClient);
		LoginCredentials credentials = context.Credentials.Copy();
		IPrivateKeySource? keySource = null;
		try
		{
			(credentials, keySource) = await PrepareCredentialsAsync(
				credentials,
				context.Interaction,
				context.Account,
				context.Options,
				Settings,
				allowPrompt: false,
				cancellationToken);
			AttemptRequest request = new()
			{
				Host = context.Host,
				Port = context.Port,
				Dial = context.Dial,
				Credentials = credentials,
				KeySource = keySource,
				TrustedFingerprint = context.HostKeyFingerprint,
				Interaction = context.Interaction,
				Settings = Settings,
				Options = context.Options,

				// The session's own route: its proxy for a direct dial, the loopback port of its last hop otherwise.
				Proxy = SshProxyDial.ForDial(context.Options.Proxy, dialsDirectly: context.Jump is null),
			};

			AttemptOutcome<TClient> outcome = await AttemptAsync(request, createClient, cancellationToken);
			return outcome.Client
				?? throw new ProtocolConnectException(ConnectFailure.AuthenticationFailed, outcome.AuthenticationFailure ?? "Authentication failed.");
		}
		finally
		{
			(keySource as IDisposable)?.Dispose();
			credentials.Dispose();
		}
	}

	private async Task<SshConnection<TClient>> ConnectCoreAsync<TClient>(
		ProtocolConnectContext context,
		Func<ConnectionInfo, TClient> createClient,
		SshEndpoint? dial,
		SshJumpTrail? trail,
		CancellationToken cancellationToken)
		where TClient : BaseClient
	{
		ArgumentNullException.ThrowIfNull(createClient);
		SshSettings settings = Settings;
		SshConnectionOptions options = SshConnectionOptions.FromOptions(context.Connection.Options);
		string host = context.Host.Address;
		int port = context.Port;
		string endpoint = HostEndpoint.Format(host, port);

		SshJumpChain? jump = null;
		SshEndpoint target = dial ?? new SshEndpoint(host, port);
		if (dial is null && options.JumpConnectionIds.Count > 0)
		{
			IConnectionResolver resolver = context.Resolver ?? throw new ProtocolConnectException(
				ConnectFailure.ProtocolError,
				"This session cannot open the jump hosts of the connection, because it has no access to the saved logins.");
			jump = await SshJumpChain.OpenAsync(this, resolver, context, options.JumpConnectionIds, target, trail ?? new SshJumpTrail(), cancellationToken);
			target = jump.Endpoint;
		}

		ProxyOptions proxy = SshProxyDial.ForDial(options.Proxy, dialsDirectly: dial is null && jump is null);
		proxy.EnsureUsable();
		if (options.Proxy.Kind != ProxyKind.None && proxy.Kind == ProxyKind.None)
		{
			_logger.LogDebug("SSH session {SessionId} leaves its proxy unused: this dial goes through a jump host", context.SessionId);
		}

		context.Status?.Report($"Connecting to {endpoint}");
		LoginCredentials? credentials = null;
		IPrivateKeySource? keySource = null;
		bool handedOver = false;
		try
		{
			credentials = await context.Credentials.GetAsync(cancellationToken) ?? throw Cancelled();
			(credentials, keySource) = await PrepareCredentialsAsync(credentials, context.Interaction, $"{credentials.Username}@{host}", options, settings, allowPrompt: true, cancellationToken);
			credentials = await ProxyLogin.EnsurePasswordAsync(credentials, proxy, context.Interaction, cancellationToken);
			string? approvedFingerprint = null;
			int attempts = 1;
			while (true)
			{
				AttemptRequest request = new()
				{
					Host = host,
					Port = port,
					Dial = target,
					Credentials = credentials,
					KeySource = keySource,
					Verifier = context.HostVerifier,
					TrustedFingerprint = approvedFingerprint,
					Interaction = context.Interaction,
					Status = context.Status,
					Settings = settings,
					Options = options,
					Proxy = proxy,
				};

				AttemptOutcome<TClient> outcome = await AttemptAsync(request, createClient, cancellationToken);
				if (outcome is { Client: { } client, Fingerprint: { } fingerprint })
				{
					SshSessionContext sessionContext = new()
					{
						SessionId = context.SessionId,
						Host = host,
						Port = port,
						Interaction = context.Interaction,
						Options = options,
						Credentials = credentials,
						HostKeyFingerprint = fingerprint,
						Jump = jump,
					};

					credentials = null;
					handedOver = true;
					_logger.LogInformation("SSH session {SessionId} connected", context.SessionId);
					return new SshConnection<TClient>(client, sessionContext);
				}

				if (outcome.PendingVerification is { } verification)
				{
					context.Status?.Report("Waiting for the host key to be confirmed");
					if (!await verification.WaitAsync(cancellationToken))
					{
						throw new ProtocolConnectException(ConnectFailure.HostIdentityRejected, $"The host key of {endpoint} was not trusted.");
					}

					// Reconnect trusting exactly the key the user saw; a different key goes through verification again.
					approvedFingerprint = outcome.Fingerprint;
					context.Status?.Report($"Connecting to {endpoint}");
					continue;
				}

				_logger.LogDebug("SSH session {SessionId} login attempt {Attempt} was rejected", context.SessionId, attempts);
				string failure = outcome.AuthenticationFailure ?? "Authentication failed.";
				if (attempts >= settings.EffectiveAuthenticationAttempts)
				{
					throw new ProtocolConnectException(ConnectFailure.AuthenticationFailed, failure);
				}

				LoginCredentials retry = await context.Credentials.RetryAsync("Authentication failed", cancellationToken)
					?? throw new ProtocolConnectException(ConnectFailure.AuthenticationFailed, failure);

				retry = ProxyLogin.CarryProxyPassword(retry, credentials);
				(keySource as IDisposable)?.Dispose();
				keySource = null;
				credentials.Dispose();
				credentials = retry;
				(credentials, keySource) = await PrepareCredentialsAsync(credentials, context.Interaction, $"{credentials.Username}@{host}", options, settings, allowPrompt: true, cancellationToken);
				attempts++;
			}
		}
		finally
		{
			(keySource as IDisposable)?.Dispose();
			credentials?.Dispose();
			if (!handedOver && jump is not null)
			{
				await jump.DisposeAsync();
			}
		}
	}

	private static ProtocolConnectException Cancelled() => new(ConnectFailure.Cancelled, "The login was cancelled.");

	/// <summary>
	/// True when the socket goes straight to the connection's own address. Behind a jump host that address is only reachable
	/// through the tunnel, so probing it from here would ask another machine, and a failure may belong to a hop anyway.
	/// Through a proxy the probe would not go where the connection went either.
	/// </summary>
	private static bool DialsDirectly(ProtocolConnectContext context, SshEndpoint? dial)
	{
		SshConnectionOptions options = SshConnectionOptions.FromOptions(context.Connection.Options);
		return dial is null && options.JumpConnectionIds.Count == 0 && !options.Proxy.IsEnabled;
	}

	/// <summary>
	/// Loads what the login signs with: a private key, asking for its passphrase when none is saved or the saved one is
	/// wrong and once more after a wrong answer, or the keys an SSH agent holds. A typed passphrase is kept in the
	/// returned credentials, which replace the input.
	/// </summary>
	private static async Task<(LoginCredentials Credentials, IPrivateKeySource? KeySource)> PrepareCredentialsAsync(
		LoginCredentials credentials,
		IUserInteraction interaction,
		string account,
		SshConnectionOptions options,
		SshSettings settings,
		bool allowPrompt,
		CancellationToken cancellationToken)
	{
		if (credentials.Method == LoginMethod.Agent)
		{
			// The agent is read with blocking calls that time out on their own; off this flow, a cancel does not wait for them.
			SshAgentKeySource agentKeys = await Task.Run(() => CreateAgentKeySource(options, settings), CancellationToken.None).WaitAsync(cancellationToken);
			return (credentials, agentKeys);
		}

		if (credentials.Method != LoginMethod.PublicKey)
		{
			return (credentials, null);
		}

		if (credentials.PrivateKey is not { } privateKey)
		{
			throw new ProtocolConnectException(ConnectFailure.AuthenticationFailed, "No private key is saved for this login.");
		}

		PrivateKeyStatus status = SshPrivateKeys.TryLoad(privateKey.Span, credentials.Passphrase?.RevealString(), out PrivateKeyFile? keyFile, out string? error);
		string? typedPassphrase = null;
		for (int prompt = 0; allowPrompt && prompt < MaxPassphrasePrompts && status is PrivateKeyStatus.PassphraseRequired or PrivateKeyStatus.WrongPassphrase; prompt++)
		{
			string message = status == PrivateKeyStatus.PassphraseRequired
				? $"The private key for {account} is protected by a passphrase."
				: prompt == 0
					? $"The saved passphrase does not unlock the private key for {account}."
					: "Wrong passphrase. Try again.";

			SecretPromptResult answer = await interaction.PromptSecretAsync(
				new SecretPrompt { Title = "Passphrase for key", Message = message, Label = "Passphrase" },
				cancellationToken) ?? throw Cancelled();

			typedPassphrase = answer.Secret;
			status = SshPrivateKeys.TryLoad(privateKey.Span, typedPassphrase, out keyFile, out error);
		}

		if (keyFile is null)
		{
			string reason = status switch
			{
				PrivateKeyStatus.PassphraseRequired => "The private key needs a passphrase.",
				PrivateKeyStatus.WrongPassphrase => "The passphrase does not unlock the private key.",
				_ => $"The private key cannot be used: {error}",
			};

			throw new ProtocolConnectException(ConnectFailure.AuthenticationFailed, reason);
		}

		if (typedPassphrase is null)
		{
			return (credentials, keyFile);
		}

		LoginCredentials withPassphrase = new()
		{
			Username = credentials.Username,
			Method = credentials.Method,
			Password = credentials.Password?.Copy(),
			PrivateKey = privateKey.Copy(),
			Passphrase = SecretBuffer.FromString(typedPassphrase),
			ProxyPassword = credentials.ProxyPassword?.Copy(),
		};

		credentials.Dispose();
		return (withPassphrase, keyFile);
	}

	/// <summary>The agent keys this login signs with. Nothing but the signature ever crosses the agent boundary.</summary>
	private static SshAgentKeySource CreateAgentKeySource(SshConnectionOptions options, SshSettings settings)
	{
		try
		{
			SshAgentSnapshot snapshot = SshAgents.Read(settings.AgentEndpoint, settings.ConnectTimeout);
			return SshAgents.CreateKeySource(snapshot, options.AgentIdentity, settings.ConnectTimeout);
		}
		catch (SshAgentException ex)
		{
			throw new ProtocolConnectException(ConnectFailure.AuthenticationFailed, ex.Message, ex);
		}
	}

	private static async Task<AttemptOutcome<TClient>> AttemptAsync<TClient>(AttemptRequest request, Func<ConnectionInfo, TClient> createClient, CancellationToken cancellationToken)
		where TClient : BaseClient
	{
		string endpoint = HostEndpoint.Format(request.Host, request.Port);
		HostKeyCheck hostKeys = new(request.Host, request.Port, request.Verifier, request.TrustedFingerprint, request.Status, cancellationToken);
		ConnectWatchdog watchdog = new(request.Settings.ConnectTimeout);
		CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, watchdog.Token);
		EventHandler<SshIdentificationEventArgs> onServerIdentified = (_, _) => watchdog.Arm();
		SshLogin? login = null;
		TClient? client = null;
		Task? connecting = null;

		void Release(TClient? failedClient)
		{
			if (failedClient is not null)
			{
				failedClient.HostKeyReceived -= hostKeys.OnHostKeyReceived;
				failedClient.ServerIdentificationReceived -= onServerIdentified;
				failedClient.Dispose();
			}

			login?.Dispose();
			linked.Dispose();
			watchdog.Dispose();
		}

		try
		{
			login = new SshLogin(request.Credentials, request.KeySource, request.Interaction, $"{request.Credentials.Username}@{request.Host}", request.Settings.PromptTimeout, watchdog, cancellationToken);
			ConnectionInfo connectionInfo = SshProxyDial.CreateConnectionInfo(
				request.Dial,
				request.Credentials.Username,
				request.Proxy,
				request.Credentials.ProxyPassword,
				login.Methods);

			// SSH.NET applies this to each wait during key exchange and login, including a user typing a one-time code.
			connectionInfo.Timeout = request.Settings.ConnectTimeout + request.Settings.PromptTimeout;

			client = createClient(connectionInfo);
			client.HostKeyReceived += hostKeys.OnHostKeyReceived;
			client.ServerIdentificationReceived += onServerIdentified;
			if (request.Settings.GetKeepAlive(request.Options) is { } keepAlive)
			{
				client.KeepAliveInterval = keepAlive;
			}

			TClient connectingClient = client;
			CancellationToken connectToken = linked.Token;
			connecting = Task.Run(() => connectingClient.ConnectAsync(connectToken), CancellationToken.None);
			await connecting.WaitAsync(connectToken);
		}
		catch (Exception ex)
		{
			bool timedOut = watchdog.TimedOut;
			if (connecting is { IsCompleted: false } && client is { } abandoned)
			{
				// SSH.NET is still inside its synchronous key exchange or login, which ignores cancellation; clean up once it gives up.
				_ = connecting.ContinueWith(
					completed =>
					{
						_ = completed.Exception;
						Release(abandoned);
					},
					CancellationToken.None,
					TaskContinuationOptions.None,
					TaskScheduler.Default);
			}
			else
			{
				Release(client);
			}

			cancellationToken.ThrowIfCancellationRequested();
			if (login?.PromptTimedOut == true)
			{
				throw new ProtocolConnectException(ConnectFailure.Timeout, $"The login prompt for {endpoint} was not answered in time.", ex);
			}

			return ClassifyFailure<TClient>(ex, hostKeys, login?.PromptCancelled == true, timedOut, request, endpoint);
		}

		client.ServerIdentificationReceived -= onServerIdentified;
		Release(null);
		if (hostKeys.AcceptedFingerprint is not { } fingerprint)
		{
			client.Dispose();
			throw new ProtocolConnectException(ConnectFailure.ProtocolError, $"{endpoint} did not present a host key.");
		}

		hostKeys.Pin();
		return AttemptOutcome<TClient>.Connected(client, fingerprint);
	}

	private static AttemptOutcome<TClient> ClassifyFailure<TClient>(Exception exception, HostKeyCheck hostKeys, bool promptCancelled, bool timedOut, AttemptRequest request, string endpoint)
		where TClient : BaseClient
	{
		if (hostKeys.Pending is { } pending)
		{
			return AttemptOutcome<TClient>.Pending(pending.Verification, pending.Fingerprint);
		}

		if (promptCancelled)
		{
			throw Cancelled();
		}

		if (hostKeys.Rejected)
		{
			throw new ProtocolConnectException(ConnectFailure.HostIdentityRejected, $"The host key of {endpoint} was not trusted.", exception);
		}

		if (hostKeys.KeyChanged)
		{
			throw new ProtocolConnectException(ConnectFailure.HostIdentityRejected, $"The host key of {endpoint} no longer matches the key this session trusted.", exception);
		}

		if (hostKeys.Error is { } error)
		{
			throw new ProtocolConnectException(ConnectFailure.HostIdentityRejected, $"The host key of {endpoint} could not be verified: {error.Message}", error);
		}

		if (timedOut || exception is OperationCanceledException)
		{
			throw new ProtocolConnectException(ConnectFailure.Timeout, $"Timed out connecting to {endpoint}.", exception);
		}

		Exception mapped = SshConnectErrors.ToConnectException(exception, request.Host, request.Port);
		if (exception is SshAuthenticationException && mapped is ProtocolConnectException authentication)
		{
			return AttemptOutcome<TClient>.Rejected(authentication.Message);
		}

		throw mapped;
	}

	private sealed record AttemptRequest
	{
		/// <summary>The target's own address, which is what the host key belongs to.</summary>
		public required string Host { get; init; }

		public required int Port { get; init; }

		/// <summary>Where the socket goes, which differs from <see cref="Host"/> behind a jump host.</summary>
		public required SshEndpoint Dial { get; init; }

		public required LoginCredentials Credentials { get; init; }

		/// <summary>The loaded key or the agent's keys; not owned.</summary>
		public IPrivateKeySource? KeySource { get; init; }

		/// <summary>Null for companion connections, which only trust <see cref="TrustedFingerprint"/>.</summary>
		public IHostIdentityVerifier? Verifier { get; init; }

		public string? TrustedFingerprint { get; init; }

		public required IUserInteraction Interaction { get; init; }

		public IProgress<string>? Status { get; init; }

		public required SshSettings Settings { get; init; }

		public required SshConnectionOptions Options { get; init; }

		/// <summary>The proxy this socket goes through, which is none for a dial that already goes to a jump host.</summary>
		public ProxyOptions Proxy { get; init; } = ProxyOptions.None;
	}

	private sealed record AttemptOutcome<TClient>(TClient? Client, string? Fingerprint, Task<bool>? PendingVerification, string? AuthenticationFailure)
		where TClient : BaseClient
	{
		public static AttemptOutcome<TClient> Connected(TClient client, string fingerprint) => new(client, fingerprint, null, null);

		public static AttemptOutcome<TClient> Pending(Task<bool> verification, string fingerprint) => new(null, fingerprint, verification, null);

		public static AttemptOutcome<TClient> Rejected(string message) => new(null, null, null, message);
	}
}
