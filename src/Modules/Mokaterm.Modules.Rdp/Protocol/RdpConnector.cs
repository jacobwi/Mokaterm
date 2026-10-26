using Devolutions.IronRdp;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// Dials an RDP server: the socket, TLS, the certificate and, unless it is turned off, the CredSSP login check, run
/// step by step by <see cref="RdpConnectSequence"/>. Everything a failure needs to say is worked out here so the shell
/// only ever sees a <see cref="ProtocolConnectException"/>. The connect probe in the samples runs this same class.
/// </summary>
internal sealed class RdpConnector
{
	private readonly IHostIdentityVerifier _verifier;
	private readonly RdpDial? _dial;
	private readonly RdpRealmFinder _realms;

	/// <param name="dial">Opens the TCP connection, <see cref="RdpDialer.ConnectAsync"/> unless a test says otherwise.</param>
	public RdpConnector(
		string host,
		int port,
		RdpConnectionOptions options,
		TimeSpan timeout,
		IHostIdentityVerifier verifier,
		RdpDial? dial = null,
		RdpRealmFinder? realms = null)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(verifier);
		Host = host;
		Port = port;
		Options = options;
		Timeout = timeout;
		_verifier = verifier;
		_dial = dial;
		_realms = realms ?? RdpKerberosRealm.FindAsync;
	}

	public string Host { get; }

	public int Port { get; }

	public RdpConnectionOptions Options { get; }

	/// <summary>Covers one attempt from the dial to the session, but not the time a certificate prompt is on screen.</summary>
	public TimeSpan Timeout { get; }

	public IProgress<string>? Trace { get; init; }

	/// <summary>The desktop size asked for at connect time, before any resize.</summary>
	public (int Width, int Height) DesktopSize => Options.DesktopSize == RdpDesktopSizeMode.Fixed
		? (Options.Width, Options.Height)
		: (RdpConnectionOptions.DefaultWidth, RdpConnectionOptions.DefaultHeight);

	/// <summary>
	/// How the last attempt signed in, or was going to, and the security protocol the server picked in it, null until a
	/// server answered the RDP request. The probe prints both, whether the attempt worked or not.
	/// </summary>
	public RdpSignInRoute? LastRoute { get; private set; }

	/// <inheritdoc cref="LastRoute"/>
	public RdpSecurityProtocols? LastNegotiated { get; private set; }

	/// <summary>
	/// How <paramref name="credentials"/> sign in with <paramref name="options"/>. A login with no password, or none at
	/// all, never sends one: the server's own sign-in screen asks for it instead.
	/// </summary>
	public static RdpSignInRoute RouteFor(RdpConnectionOptions options, LoginCredentials? credentials)
	{
		ArgumentNullException.ThrowIfNull(options);
		if (credentials is null || credentials.Method == AuthenticationMethod.Anonymous)
		{
			return RdpSignInRoute.SignInScreen;
		}

		if (options.NetworkLevelAuthentication)
		{
			return RdpSignInRoute.NetworkLevelAuthentication;
		}

		return options.Autologon && credentials.Password is { Length: > 0 } ? RdpSignInRoute.Automatic : RdpSignInRoute.SignInScreen;
	}

	/// <exception cref="ProtocolConnectException">Connecting failed in a way the user should see.</exception>
	/// <exception cref="RdpAuthenticationException">The server refused the login and another password may work.</exception>
	public async Task<RdpTransport> ConnectAsync(LoginCredentials? credentials, IProgress<string>? status, CancellationToken cancellationToken)
	{
		RdpSignInRoute route = RouteFor(Options, credentials);
		LastRoute = route;
		LastNegotiated = null;

		// The names only, for the messages: the password stays in the credentials until the config is built.
		RdpLogin names = RdpLogin.Parse(credentials?.Username, Options.Domain, null);
		if (route == RdpSignInRoute.NetworkLevelAuthentication && names.CredsspRefusal is { } refusal)
		{
			// Refused before anything is dialed, so the prompt that follows can take a name that works. An Entra ID
			// account needs network level authentication turned off, which no other name in that prompt would change.
			throw names.IsEntraId
				? new ProtocolConnectException(ConnectFailure.AuthenticationFailed, refusal)
				: new RdpAuthenticationException(refusal);
		}

		RdpRealm? realm = null;
		if (route == RdpSignInRoute.NetworkLevelAuthentication)
		{
			realm = await _realms(names.Domain, Host, cancellationToken);
			if (realm is not null)
			{
				Trace?.Report($"Domain {names.Domain} is {realm.DnsName}, from {realm.Source}");
			}
		}

		for (int attempt = 1; ; attempt++)
		{
			RdpConnectProgress progress = new(Host, Port, status, Trace);
			using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			deadline.CancelAfter(Timeout);
			await using RdpConnectSequence sequence = new(
				progress,
				new DeadlinePausingVerifier(_verifier, deadline, Timeout, cancellationToken),
				_dial);
			try
			{
				using Config config = BuildConfig(credentials, route, realm);
				return await sequence.RunAsync(config, route, Timeout, deadline.Token);
			}
			catch (Exception ex) when (attempt == 1 && ServerGaveUpDuringPrompt(ex, sequence, progress, deadline, cancellationToken))
			{
				// The server stopped waiting while the user read the certificate. The answer is remembered for this
				// window, so dialing again goes straight on to the login.
				status?.Report("Connecting again after the certificate check");
			}
			catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
			{
				// Whatever a step threw on its way out of a cancel by the user, the attempt was cancelled, not failed.
				cancellationToken.ThrowIfCancellationRequested();
				throw await DescribeAsync(ex, progress, sequence, names, route, realm, deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested);
			}
			finally
			{
				LastNegotiated = sequence.Negotiated;
			}
		}
	}

	private static bool ServerGaveUpDuringPrompt(
		Exception exception,
		RdpConnectSequence sequence,
		RdpConnectProgress progress,
		CancellationTokenSource deadline,
		CancellationToken cancellationToken) =>
		sequence.CertificatePrompted
		&& !deadline.IsCancellationRequested
		&& !cancellationToken.IsCancellationRequested
		&& progress.Stage is RdpConnectStage.Login or RdpConnectStage.Session
		&& sequence.LoginMessagesSent <= 1
		&& RdpConnectErrors.IsConnectionLoss(exception);

	private static async Task<Exception> DescribeAsync(
		Exception exception,
		RdpConnectProgress progress,
		RdpConnectSequence sequence,
		RdpLogin names,
		RdpSignInRoute route,
		RdpRealm? realm,
		bool timedOut)
	{
		if (!timedOut
			&& route == RdpSignInRoute.NetworkLevelAuthentication
			&& progress.Stage == RdpConnectStage.Login
			&& RdpConnectErrors.IsLoginRefused(exception, sequence.LoginMessagesSent))
		{
			return exception as RdpAuthenticationException
				?? new RdpAuthenticationException(RdpConnectErrors.LoginRefused(exception, progress, names), exception);
		}

		Exception described = await RdpConnectErrors.DescribeAsync(exception, progress, timedOut, CancellationToken.None);
		return timedOut
			&& route == RdpSignInRoute.NetworkLevelAuthentication
			&& progress.Stage is RdpConnectStage.Login or RdpConnectStage.Kerberos
			&& realm is null
			&& RdpKerberosRealm.IsShortDomain(names.Domain, progress.Host)
			&& described is ProtocolConnectException connect
			? new ProtocolConnectException(connect.Failure, connect.Message + " " + ShortDomainHint(names), connect.InnerException)
			: described;
	}

	private static string ShortDomainHint(RdpLogin names) =>
		$"{names.Domain} is the domain's short name, which no Kerberos server could be found by. Write the login as " +
		$"{names.Username}@ and the domain's full name, the end of the PC's full name (for pc.corp.example.com that is " +
		$"{names.Username}@corp.example.com).";

	private Config BuildConfig(LoginCredentials? credentials, RdpSignInRoute route, RdpRealm? realm)
	{
		(int width, int height) = DesktopSize;

		// The password only exists as a string for as long as the builder needs it; nothing else keeps a copy. IronRDP
		// keeps its own in the connection's config for the session's life, on both routes that send one.
		string password = route == RdpSignInRoute.SignInScreen ? "" : credentials?.Password?.RevealString() ?? "";
		RdpLogin login = RdpLogin.Parse(credentials?.Username, Options.Domain, password);
		if (realm is not null)
		{
			login = login with { Domain = realm.DnsName };
		}

		return RdpConfigFactory.Build(Options, login, width, height, route);
	}
}
