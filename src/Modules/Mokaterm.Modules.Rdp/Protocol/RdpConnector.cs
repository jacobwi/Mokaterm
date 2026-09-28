using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Devolutions.IronRdp;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// Dials an RDP server: the socket, TLS and, unless it is turned off, the CredSSP login check, all inside
/// IronRDP. Everything a failure needs to say is worked out here so the shell only ever sees a
/// <see cref="ProtocolConnectException"/>.
/// </summary>
internal sealed class RdpConnector
{
	private readonly IHostIdentityVerifier _verifier;

	public RdpConnector(string host, int port, RdpConnectionOptions options, TimeSpan timeout, IHostIdentityVerifier verifier)
	{
		ArgumentNullException.ThrowIfNull(options);
		Host = host;
		Port = port;
		Options = options;
		Timeout = timeout;
		_verifier = verifier;
	}

	public string Host { get; }

	public int Port { get; }

	public RdpConnectionOptions Options { get; }

	/// <summary>Covers the connection, the TLS handshake and the login check together.</summary>
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
		(int requestedWidth, int requestedHeight) = DesktopSize;
		Config? config = BuildConfig(credentials, credssp, requestedWidth, requestedHeight);
		ConnectionResult? result = null;
		SslStream? stream = null;
		try
		{
			status?.Report(credssp ? "Connecting and checking the login" : "Connecting");
			Task<(ConnectionResult, Framed<SslStream>)> connect = Connection.Connect(config, Host, null, Port);
			Framed<SslStream> framed;
			try
			{
				(result, framed) = await WaitForConnectAsync(connect, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				// Connection.Connect takes no cancellation token, so the attempt runs on with nobody waiting for it,
				// and it reads the config until it has built its connector: the config goes with what it produces.
				Abandon(connect, config);
				config = null;
				if (cancellationToken.IsCancellationRequested)
				{
					throw;
				}

				throw new TimeoutException($"{HostEndpoint.Format(Host, Port)} did not finish the RDP connection in time.");
			}

			stream = framed.GetInner().Item1;

			// The server decides the desktop size, and a huge one would have the decoder allocate gigabytes.
			int width;
			int height;
			using (DesktopSize size = result.GetDesktopSize())
			{
				width = size.GetWidth();
				height = size.GetHeight();
			}

			if (!RdpDesktopBounds.IsSupported(width, height))
			{
				throw new ProtocolConnectException(ConnectFailure.ProtocolError, RdpDesktopBounds.DescribeRefusal(width, height));
			}

			status?.Report("Checking the server");
			HostIdentity? identity = ReadCertificateIdentity(stream, Host, Port);
			bool verified = false;
			if (identity is not null)
			{
				verified = await _verifier.VerifyAsync(identity, cancellationToken);
				if (!verified)
				{
					throw new ProtocolConnectException(
						ConnectFailure.HostIdentityRejected,
						$"The certificate {Host}:{Port} presented was not accepted.");
				}
			}

			RdpTransport transport = new(config, result, framed, stream, width, height, credssp, identity, verified);
			result = null;
			stream = null;
			return transport;
		}
		catch (Exception ex)
		{
			config?.Dispose();
			result?.Dispose();
			if (stream is not null)
			{
				await stream.DisposeAsync();
			}

			if (ex is RdpAuthenticationException || (ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
			{
				throw;
			}

			if (RdpConnectErrors.IsAuthenticationFailure(ex))
			{
				throw new RdpAuthenticationException(LoginMessage(ex, credssp), ex);
			}

			throw await RdpConnectErrors.DescribeAsync(ex, Host, Port, ex is TimeoutException, CancellationToken.None);
		}
	}

	/// <summary>
	/// The certificate the server presented, or null when it skipped TLS. IronRDP finishes the handshake itself and
	/// accepts whatever arrives, because almost every RDP server is self-signed, so the chain is judged here and the
	/// answer goes to the user.
	/// </summary>
	private static HostIdentity? ReadCertificateIdentity(SslStream stream, string host, int port)
	{
		using X509Certificate2? certificate = TlsHostIdentity.Read(stream);
		return certificate is null
			? null
			: TlsHostIdentity.Create(host, port, certificate, TlsHostIdentity.IsChainTrusted(certificate, host));
	}

	private static string LoginMessage(Exception exception, bool credssp)
	{
		string detail = exception is IronRdpException ironRdp ? ironRdp.Inner.ToDisplay() : exception.Message;
		return credssp
			? $"The server refused the login: {detail}"
			: $"The server refused the connection: {detail}";
	}

	private Config BuildConfig(LoginCredentials? credentials, bool credssp, int width, int height)
	{
		// The password only exists as a string for as long as the builder needs it; nothing else keeps a copy.
		string password = credentials?.Password?.RevealString() ?? "";
		RdpLogin login = RdpLogin.Parse(credentials?.Username, Options.Domain, credssp ? password : "");
		return RdpConfigFactory.Build(Options, login, width, height, credssp);
	}

	private async Task<(ConnectionResult Result, Framed<SslStream> Framed)> WaitForConnectAsync(
		Task<(ConnectionResult, Framed<SslStream>)> connect,
		CancellationToken cancellationToken)
	{
		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(Timeout);
		return await connect.WaitAsync(deadline.Token);
	}

	/// <summary>Closes whatever an attempt nobody is waiting for still manages to produce, and then its config.</summary>
	private static void Abandon(Task<(ConnectionResult, Framed<SslStream>)> connect, Config config) =>
		_ = connect.ContinueWith(
			finished =>
			{
				try
				{
					if (!finished.IsCompletedSuccessfully)
					{
						// Reading the exception keeps it from being reported as unobserved later.
						_ = finished.Exception;
						return;
					}

					(ConnectionResult result, Framed<SslStream> framed) = finished.Result;
					result.Dispose();
					framed.GetInner().Item1.Dispose();
				}
				catch (Exception ex) when (ex is IOException or ObjectDisposedException)
				{
					// Nothing left to close.
				}
				finally
				{
					config.Dispose();
				}
			},
			CancellationToken.None,
			TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);
}
