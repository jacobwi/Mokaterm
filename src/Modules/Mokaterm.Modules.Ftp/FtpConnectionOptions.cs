using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Ftp;

/// <summary>Typed access to the <c>ftp.*</c> keys an FTP connection keeps in its <see cref="ProtocolOptions"/>.</summary>
public sealed record FtpConnectionOptions
{
	public const string EncryptionKey = "ftp.encryption";

	public const string DataConnectionKey = "ftp.dataConnection";

	public const string EncodingKey = "ftp.encoding";

	public const string InitialDirectoryKey = "ftp.initialDirectory";

	/// <summary>The proxy group: <c>ftp.proxy.kind</c>, <c>.host</c>, <c>.port</c> and <c>.user</c>.</summary>
	public const string ProxyKeyPrefix = "ftp.proxy.";

	/// <summary>Character set used when none is stored.</summary>
	public const string DefaultEncodingName = "utf-8";

	/// <summary>Port used for implicit FTPS when the connection leaves the port empty.</summary>
	public const int ImplicitTlsPort = 990;

	public static FtpConnectionOptions Default { get; } = new();

	public FtpEncryption Encryption { get; init; }

	public FtpDataConnectionMode DataConnection { get; init; }

	/// <summary>Character set for commands and file names, such as <c>utf-8</c> or <c>windows-1252</c>.</summary>
	public string EncodingName { get; init; } = DefaultEncodingName;

	/// <summary>Where the file browser starts. Relative paths start at the login directory; null uses the login directory.</summary>
	public string? InitialDirectory { get; init; }

	/// <summary>
	/// The proxy every connection of this session goes through, the data connections included: FluentFTP dials a data
	/// connection with a clone of the client, so it takes the same route as the control connection.
	/// </summary>
	public ProxyOptions Proxy { get; init; } = ProxyOptions.None;

	public static FtpConnectionOptions From(ProtocolOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		string? encoding = options.GetString(EncodingKey);
		string? initialDirectory = options.GetString(InitialDirectoryKey);
		return new FtpConnectionOptions
		{
			Encryption = options.GetEnum(EncryptionKey, FtpEncryption.None),
			DataConnection = options.GetEnum(DataConnectionKey, FtpDataConnectionMode.Passive),
			EncodingName = string.IsNullOrWhiteSpace(encoding) ? DefaultEncodingName : encoding,
			InitialDirectory = string.IsNullOrWhiteSpace(initialDirectory) ? null : initialDirectory,
			Proxy = ProxyOptions.From(options, ProxyKeyPrefix),
		};
	}

	/// <summary>Writes these values over <paramref name="options"/>, keeping keys that belong to anything else.</summary>
	public ProtocolOptions ApplyTo(ProtocolOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		ProtocolOptions written = options
			.WithEnum<FtpEncryption>(EncryptionKey, Encryption)
			.WithEnum<FtpDataConnectionMode>(DataConnectionKey, DataConnection)
			.With(EncodingKey, string.IsNullOrWhiteSpace(EncodingName) ? null : EncodingName)
			.With(InitialDirectoryKey, string.IsNullOrWhiteSpace(InitialDirectory) ? null : InitialDirectory);

		return Proxy.ApplyTo(written, ProxyKeyPrefix);
	}
}
