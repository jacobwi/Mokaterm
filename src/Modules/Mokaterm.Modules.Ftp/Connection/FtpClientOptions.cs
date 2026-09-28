using System.Security.Authentication;
using System.Text;
using FluentFTP;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Ftp.Connection;

/// <summary>Everything needed to open another connection like the session's first one.</summary>
internal sealed record FtpClientOptions
{
	public required string Host { get; init; }

	public required int Port { get; init; }

	public FtpEncryption Encryption { get; init; }

	public FtpDataConnectionMode DataConnection { get; init; }

	public required Encoding Encoding { get; init; }

	public required TimeSpan ConnectTimeout { get; init; }

	public required TimeSpan DataTimeout { get; init; }

	/// <summary>The proxy every connection of the session goes through. The password comes with the credentials.</summary>
	public ProxyOptions Proxy { get; init; } = ProxyOptions.None;

	public static FtpClientOptions Create(string host, int port, FtpConnectionOptions connection, FtpSettings settings) => new()
	{
		Host = host,
		Port = port,
		Encryption = connection.Encryption,
		DataConnection = connection.DataConnection,
		Encoding = FtpEncodings.Resolve(connection.EncodingName),
		ConnectTimeout = TimeSpan.FromSeconds(settings.ConnectTimeoutSeconds),
		DataTimeout = TimeSpan.FromSeconds(settings.DataTimeoutSeconds),
		Proxy = connection.Proxy,
	};

	public FtpConfig CreateConfig() => new()
	{
		EncryptionMode = Encryption switch
		{
			FtpEncryption.Explicit => FtpEncryptionMode.Explicit,
			FtpEncryption.Implicit => FtpEncryptionMode.Implicit,
			_ => FtpEncryptionMode.None,
		},
		DataConnectionType = DataConnection switch
		{
			FtpDataConnectionMode.ExtendedPassive => FtpDataConnectionType.EPSV,
			FtpDataConnectionMode.Active => FtpDataConnectionType.AutoActive,

			// PASVEX connects to the server this session is connected to and ignores the address in the reply. Plain
			// PASV follows any routable address the server names, which lets a hostile server point the data
			// connection, and the upload in it, at another machine; on the web host that is any machine the server
			// running Mokaterm can reach.
			_ => FtpDataConnectionType.PASVEX,
		},
		ConnectTimeout = ToMilliseconds(ConnectTimeout),
		ReadTimeout = ToMilliseconds(DataTimeout),
		WriteTimeout = ToMilliseconds(DataTimeout),
		DataConnectionConnectTimeout = ToMilliseconds(ConnectTimeout),
		DataConnectionReadTimeout = ToMilliseconds(DataTimeout),
		DataConnectionWriteTimeout = ToMilliseconds(DataTimeout),

		// Let the operating system pick the TLS versions instead of FluentFTP's list, which includes TLS 1.0 and 1.1.
		SslProtocols = SslProtocols.None,
		ValidateAnyCertificate = false,

		// The session runs its own keepalive so failures can end the session.
		Noop = false,

		// The file system reconnects itself: it knows which commands are safe to repeat, and FluentFTP's reconnect
		// restores the last working folder, which fails for good once that folder is deleted.
		SelfConnectMode = FtpSelfConnectMode.Never,
		LogHost = false,
		LogUserName = false,
		LogPassword = false,
	};

	private static int ToMilliseconds(TimeSpan value) => (int)Math.Min(value.TotalMilliseconds, int.MaxValue);
}
