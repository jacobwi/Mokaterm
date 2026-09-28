using System.Net;
using FluentFTP;
using FluentFTP.Proxy.AsyncProxy;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Ftp.FileSystem;

namespace Mokaterm.Modules.Ftp.Connection;

/// <summary>Creates and connects FluentFTP clients that share one configuration and one certificate trust.</summary>
internal sealed class FtpClientFactory
{
	public const string AnonymousUserName = "anonymous";

	// Each certificate the user approves costs a reconnect; a server that keeps presenting new ones is not worth chasing.
	private const int MaxCertificateRounds = 3;

	// The reply to QUIT is a courtesy. Without a bound, a server that stopped answering holds every close for the
	// whole read timeout, once per connection.
	private static readonly TimeSpan QuitWait = TimeSpan.FromSeconds(2);

	private readonly FtpCertificateTrust _trust;
	private readonly IFtpLogger? _protocolLog;

	public FtpClientFactory(FtpClientOptions options, FtpCertificateTrust trust, ILogger protocolLogger)
	{
		Options = options;
		_trust = trust;
		_protocolLog = protocolLogger.IsEnabled(LogLevel.Trace) ? new FtpLogBridge(protocolLogger) : null;
	}

	public FtpClientOptions Options { get; }

	/// <summary>The FTP user name for <paramref name="credentials"/>. A blank name logs in anonymously.</summary>
	public static string GetUserName(LoginCredentials credentials)
	{
		ArgumentNullException.ThrowIfNull(credentials);
		return string.IsNullOrWhiteSpace(credentials.Username) ? AnonymousUserName : credentials.Username;
	}

	/// <summary>
	/// True when the server still answers on this connection. Servers and routers drop idle connections without telling
	/// the client, which FluentFTP only notices once a command fails.
	/// </summary>
	public static async Task<bool> IsAliveAsync(AsyncFtpClient client, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(client);
		if (!client.IsConnected)
		{
			return false;
		}

		try
		{
			FtpReply reply = await client.Execute("NOOP", cancellationToken);
			return reply.Code != "421";
		}
		catch (Exception ex) when (FtpErrors.IsNoAnswer(ex))
		{
			return false;
		}
	}

	/// <summary>Sends QUIT, waits a moment for the reply and closes the connection whether or not one came.</summary>
	public static async ValueTask CloseAsync(AsyncFtpClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		using CancellationTokenSource quit = new(QuitWait);
		try
		{
			await client.Disconnect(quit.Token);
		}
		catch (Exception)
		{
			// The goodbye failed or went unanswered; the connection is closed below either way.
		}

		await DiscardAsync(client);
	}

	/// <summary>Closes a client without QUIT, for connections that failed or were abandoned in the middle of a command.</summary>
	public static async ValueTask DiscardAsync(AsyncFtpClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		client.Config.DisconnectWithQuit = false;
		try
		{
			await client.DisposeAsync();
		}
		catch (Exception)
		{
			// Closing a broken connection can fail again; the client is gone either way.
		}
	}

	public AsyncFtpClient Create(LoginCredentials credentials)
	{
		ArgumentNullException.ThrowIfNull(credentials);

		// FluentFTP keeps the password as a string for its automatic reconnects, so it has to be revealed here.
		NetworkCredential login = new(GetUserName(credentials), credentials.Password?.RevealString() ?? "");
		AsyncFtpClient client = Options.Proxy.IsEnabled
			? CreateThroughProxy(login, credentials.ProxyPassword)
			: new AsyncFtpClient(Options.Host, login, Options.Port, Options.CreateConfig(), _protocolLog);

		client.Encoding = Options.Encoding;
		client.ValidateCertificate += _trust.OnValidateCertificate;
		return client;
	}

	/// <summary>
	/// A client of the FluentFTP subclass that speaks the proxy's handshake before the FTP greeting. The subclasses take
	/// only a profile, so everything else is set afterwards, and the FTP host and port go in the profile as well because
	/// FluentFTP clones the client for each data connection.
	/// </summary>
	private AsyncFtpClient CreateThroughProxy(NetworkCredential login, SecretBuffer? proxyPassword)
	{
		ProxyOptions proxy = Options.Proxy;
		FtpProxyProfile profile = new()
		{
			ProxyHost = proxy.Host!,
			ProxyPort = proxy.EffectivePort,
			ProxyCredentials = proxy.NeedsLogin ? new NetworkCredential(proxy.User, proxyPassword?.RevealString() ?? "") : null,
			FtpHost = Options.Host,
			FtpPort = Options.Port,
			FtpCredentials = login,
		};

		AsyncFtpClient client = proxy.Kind switch
		{
			ProxyKind.Socks4 => new AsyncFtpClientSocks4Proxy(profile),
			ProxyKind.Socks5 => new AsyncFtpClientSocks5Proxy(profile),
			_ => new AsyncFtpClientHttp11Proxy(profile),
		};

		client.Host = Options.Host;
		client.Port = Options.Port;
		client.Credentials = login;
		client.Config = Options.CreateConfig();
		client.Logger = _protocolLog;
		return client;
	}

	/// <summary>
	/// Connects and logs in. When a handshake was refused because the user was still being asked about the certificate,
	/// waits for the answer and reconnects with that certificate approved.
	/// </summary>
	/// <exception cref="ProtocolConnectException">The certificate was rejected.</exception>
	public async Task<AsyncFtpClient> ConnectAsync(LoginCredentials credentials, IProgress<string>? status, CancellationToken cancellationToken)
	{
		for (int round = 1; ; round++)
		{
			int refusalsBefore = _trust.RefusalCount;
			AsyncFtpClient client = Create(credentials);
			try
			{
				await client.Connect(cancellationToken);
				return client;
			}
			catch (Exception)
			{
				await DiscardAsync(client);
				FtpCertificateDecision? refused = _trust.RefusalCount != refusalsBefore ? _trust.LastRefused : null;
				if (refused is null || cancellationToken.IsCancellationRequested)
				{
					throw;
				}

				if (round >= MaxCertificateRounds)
				{
					throw CertificateRejected();
				}

				if (!refused.IsDecided)
				{
					status?.Report("Waiting for the certificate to be approved");
				}

				if (!await refused.WaitAsync(cancellationToken))
				{
					throw CertificateRejected();
				}

				_trust.Approve(refused.Identity.Fingerprint);
				status?.Report("Connecting");
			}
		}
	}

	private ProtocolConnectException CertificateRejected() =>
		new(ConnectFailure.HostIdentityRejected, $"The TLS certificate presented by {Options.Host}:{Options.Port} was not trusted.");
}
