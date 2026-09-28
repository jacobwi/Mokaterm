using FluentFTP;
using FluentFTP.Exceptions;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Ftp.Connection;
using Mokaterm.Modules.Ftp.FileSystem;

namespace Mokaterm.Modules.Ftp;

/// <summary>Opens FTP and FTPS sessions.</summary>
internal sealed class FtpProtocolProvider : IProtocolProvider
{
	public const string ProtocolId = "ftp";

	public const int DefaultPort = 21;

	private readonly ISettingsService _settings;
	private readonly TimeProvider _timeProvider;
	private readonly ILoggerFactory _loggerFactory;
	private readonly ILogger _logger;

	public FtpProtocolProvider(ISettingsService settings, TimeProvider timeProvider, ILoggerFactory loggerFactory)
	{
		_settings = settings;
		_timeProvider = timeProvider;
		_loggerFactory = loggerFactory;
		_logger = loggerFactory.CreateLogger<FtpProtocolProvider>();
	}

	public ProtocolDescriptor Descriptor { get; } = new()
	{
		Id = ProtocolId,
		DisplayName = "FTP",
		Description = "File transfer over FTP, optionally secured with TLS (FTPS).",
		DefaultPort = DefaultPort,
		Capabilities = ProtocolCapabilities.FileSystem,
		AuthenticationMethods = [AuthenticationMethod.Password, AuthenticationMethod.Anonymous],
		RequiresUsername = false,
		Order = 10,
	};

	/// <summary>How long a TLS handshake waits for the host verifier. Tests shorten it.</summary>
	internal TimeSpan CertificateDecisionWait { get; init; } = FtpCertificateTrust.DefaultDecisionWait;

	public async Task<IProtocolSession> ConnectAsync(ProtocolConnectContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		FtpSettings settings = _settings.Get<FtpSettings>().Clamped();
		FtpConnectionOptions connection = FtpConnectionOptions.From(context.Connection.Options);
		if (!FtpEncodings.TryGet(connection.EncodingName, out _))
		{
			_logger.LogWarning("Unknown FTP character set {Encoding}; using UTF-8", connection.EncodingName);
		}

		connection.Proxy.EnsureUsable();

		// An empty port means the protocol default, which for implicit FTPS is 990 rather than 21.
		int port = context.Connection.Port ?? (connection.Encryption == FtpEncryption.Implicit ? FtpConnectionOptions.ImplicitTlsPort : context.Port);
		FtpClientOptions options = FtpClientOptions.Create(context.Host.Address, port, connection, settings);
		FtpCertificateTrust trust = new(context.HostVerifier, options.Host, options.Port, _loggerFactory.CreateLogger<FtpCertificateTrust>(), CertificateDecisionWait);
		try
		{
			return await ConnectCoreAsync(context, options, connection, settings, trust, cancellationToken);
		}
		catch
		{
			trust.Dispose();
			throw;
		}
	}

	private static async ValueTask<LoginCredentials?> GetCredentialsAsync(ICredentialSource source, CancellationToken cancellationToken)
	{
		if (source.Method == AuthenticationMethod.Anonymous)
		{
			return new LoginCredentials
			{
				Username = string.IsNullOrWhiteSpace(source.Username) ? FtpClientFactory.AnonymousUserName : source.Username,
				Method = AuthenticationMethod.Anonymous,
			};
		}

		return await source.GetAsync(cancellationToken);
	}

	private static void EnsureSupported(LoginCredentials credentials)
	{
		if (credentials.Method is AuthenticationMethod.PublicKey or AuthenticationMethod.Agent)
		{
			throw new ProtocolConnectException(ConnectFailure.AuthenticationFailed, "FTP logs in with a password or anonymously; key-based logins are not available.");
		}
	}

	private async Task<IProtocolSession> ConnectCoreAsync(
		ProtocolConnectContext context,
		FtpClientOptions options,
		FtpConnectionOptions connection,
		FtpSettings settings,
		FtpCertificateTrust trust,
		CancellationToken cancellationToken)
	{
		FtpClientFactory factory = new(options, trust, _loggerFactory.CreateLogger("Mokaterm.Modules.Ftp.Protocol"));
		LoginCredentials? credentials = null;
		AsyncFtpClient? client = null;
		long attemptStarted = _timeProvider.GetTimestamp();
		try
		{
			context.Status?.Report("Connecting");
			credentials = await GetCredentialsAsync(context.Credentials, cancellationToken)
				?? throw new ProtocolConnectException(ConnectFailure.Cancelled, "The login was cancelled.");
			credentials = await ProxyLogin.EnsurePasswordAsync(credentials, connection.Proxy, context.Interaction, cancellationToken);

			for (int attempt = 1; client is null; attempt++)
			{
				EnsureSupported(credentials);
				attemptStarted = _timeProvider.GetTimestamp();
				try
				{
					client = await factory.ConnectAsync(credentials, context.Status, cancellationToken);
				}
				catch (FtpAuthenticationException ex) when (FtpErrors.IsLoginRejected(ex))
				{
					string reason = FtpConnectErrors.DescribeLoginFailure(ex);
					if (attempt >= settings.AuthenticationAttempts)
					{
						throw new ProtocolConnectException(ConnectFailure.AuthenticationFailed, reason, ex);
					}

					context.Status?.Report("Waiting for credentials");
					LoginCredentials retry = await context.Credentials.RetryAsync(reason, cancellationToken)
						?? throw new ProtocolConnectException(ConnectFailure.AuthenticationFailed, reason, ex);

					retry = ProxyLogin.CarryProxyPassword(retry, credentials);
					credentials.Dispose();
					credentials = retry;
					context.Status?.Report("Connecting");
				}
			}

			context.Status?.Report("Opening the home folder");
			string loginDirectory = FtpPaths.ReadLoginDirectory(await client.GetWorkingDirectory(cancellationToken));
			string homeDirectory = string.IsNullOrWhiteSpace(connection.InitialDirectory)
				? loginDirectory
				: FtpPaths.Resolve(connection.InitialDirectory.Trim(), loginDirectory);

			// The session keeps its own copy for transfer connections and reconnects; the one from the source is disposed below.
			LoginCredentials sessionCredentials = credentials.Copy();
			try
			{
				FtpFileSystem fileSystem = new(
					client,
					token => factory.ConnectAsync(sessionCredentials, status: null, token),
					() => _settings.Get<FileTransferSettings>().MaxConcurrentTransfers,
					FtpClientFactory.GetUserName(sessionCredentials),
					loginDirectory,
					homeDirectory,
					options.DataTimeout,
					_timeProvider,
					_loggerFactory.CreateLogger<FtpFileSystem>());
				client = null;

				FtpSession session = new(fileSystem, trust, sessionCredentials, TimeSpan.FromSeconds(settings.KeepAliveSeconds), _timeProvider, _loggerFactory.CreateLogger<FtpSession>());
				session.Start();
				return session;
			}
			catch
			{
				sessionCredentials.Dispose();
				throw;
			}
		}
		catch (Exception ex) when (ex is not ProtocolConnectException && !(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
		{
			throw await FtpConnectErrors.DescribeAsync(ex, options, _timeProvider.GetElapsedTime(attemptStarted), cancellationToken);
		}
		finally
		{
			credentials?.Dispose();
			if (client is not null)
			{
				await FtpClientFactory.DiscardAsync(client);
			}
		}
	}
}
