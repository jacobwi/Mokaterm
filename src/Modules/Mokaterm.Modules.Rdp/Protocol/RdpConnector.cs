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

	/// <param name="dial">Opens the TCP connection, <see cref="RdpDialer.ConnectAsync"/> unless a test says otherwise.</param>
	public RdpConnector(string host, int port, RdpConnectionOptions options, TimeSpan timeout, IHostIdentityVerifier verifier, RdpDial? dial = null)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(verifier);
		Host = host;
		Port = port;
		Options = options;
		Timeout = timeout;
		_verifier = verifier;
		_dial = dial;
	}

	public string Host { get; }

	public int Port { get; }

	public RdpConnectionOptions Options { get; }

	/// <summary>Covers one attempt from the dial to the session, but not the time a certificate prompt is on screen.</summary>
	public TimeSpan Timeout { get; }

	/// <summary>The desktop size asked for at connect time, before any resize.</summary>
	public (int Width, int Height) DesktopSize => Options.DesktopSize == RdpDesktopSizeMode.Fixed
		? (Options.Width, Options.Height)
		: (RdpConnectionOptions.DefaultWidth, RdpConnectionOptions.DefaultHeight);

	/// <exception cref="ProtocolConnectException">Connecting failed in a way the user should see.</exception>
	/// <exception cref="RdpAuthenticationException">The server refused the login and another password may work.</exception>
	public async Task<RdpTransport> ConnectAsync(LoginCredentials? credentials, IProgress<string>? status, CancellationToken cancellationToken)
	{
		bool credssp = Options.NetworkLevelAuthentication && credentials?.Method != AuthenticationMethod.Anonymous;

		// The names only, for the messages: the password stays in the credentials until the config is built.
		RdpLogin names = RdpLogin.Parse(credentials?.Username, Options.Domain, null);
		if (credssp && names.CredsspRefusal is { } refusal)
		{
			// Refused before anything is dialed, so the prompt that follows can take a name that works.
			throw new RdpAuthenticationException(refusal);
		}

		for (int attempt = 1; ; attempt++)
		{
			RdpConnectProgress progress = new(Host, Port, status);
			using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			deadline.CancelAfter(Timeout);
			await using RdpConnectSequence sequence = new(
				progress,
				new DeadlinePausingVerifier(_verifier, deadline, Timeout, cancellationToken),
				_dial);
			try
			{
				using Config config = BuildConfig(credentials, credssp);
				return await sequence.RunAsync(config, Timeout, deadline.Token);
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
				throw await DescribeAsync(ex, progress, sequence, names, credssp, deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested);
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
		bool credssp,
		bool timedOut)
	{
		if (!timedOut && credssp && progress.Stage == RdpConnectStage.Login && RdpConnectErrors.IsLoginRefused(exception, sequence.LoginMessagesSent))
		{
			return exception as RdpAuthenticationException
				?? new RdpAuthenticationException(RdpConnectErrors.LoginRefused(exception, progress, names), exception);
		}

		return await RdpConnectErrors.DescribeAsync(exception, progress, timedOut, CancellationToken.None);
	}

	private Config BuildConfig(LoginCredentials? credentials, bool credssp)
	{
		(int width, int height) = DesktopSize;

		// The password only exists as a string for as long as the builder needs it; nothing else keeps a copy.
		string password = credssp ? credentials?.Password?.RevealString() ?? "" : "";
		RdpLogin login = RdpLogin.Parse(credentials?.Username, Options.Domain, password);
		return RdpConfigFactory.Build(Options, login, width, height, credssp);
	}
}
