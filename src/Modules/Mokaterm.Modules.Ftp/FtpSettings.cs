using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Modules.Ftp;

/// <summary>Timeouts, keepalive and login retries shared by every FTP and FTPS connection.</summary>
public sealed record FtpSettings : ISettingsSection
{
	public const int MinTimeoutSeconds = 1;

	public const int MaxConnectTimeoutSeconds = 300;

	public const int MaxDataTimeoutSeconds = 3600;

	public const int MaxKeepAliveSeconds = 3600;

	public const int MaxAuthenticationAttempts = 10;

	public static string SectionKey => "ftp";

	/// <summary>How long to wait for the server to accept a control or data connection.</summary>
	public int ConnectTimeoutSeconds { get; init; } = 15;

	/// <summary>How long to wait for a reply or for file data before giving up.</summary>
	public int DataTimeoutSeconds { get; init; } = 30;

	/// <summary>Idle seconds before a NOOP keeps the control connection alive. 0 turns keepalive off.</summary>
	public int KeepAliveSeconds { get; init; } = 60;

	/// <summary>Login attempts, the first one included, before a rejected login is final.</summary>
	public int AuthenticationAttempts { get; init; } = 3;

	/// <summary>A copy with every value inside the range the settings page allows.</summary>
	public FtpSettings Clamped() => this with
	{
		ConnectTimeoutSeconds = Math.Clamp(ConnectTimeoutSeconds, MinTimeoutSeconds, MaxConnectTimeoutSeconds),
		DataTimeoutSeconds = Math.Clamp(DataTimeoutSeconds, MinTimeoutSeconds, MaxDataTimeoutSeconds),
		KeepAliveSeconds = Math.Clamp(KeepAliveSeconds, 0, MaxKeepAliveSeconds),
		AuthenticationAttempts = Math.Clamp(AuthenticationAttempts, 1, MaxAuthenticationAttempts),
	};
}
