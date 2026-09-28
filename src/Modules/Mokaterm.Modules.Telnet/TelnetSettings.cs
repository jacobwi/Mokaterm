using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Modules.Telnet;

/// <summary>Timeouts and idle behaviour shared by every telnet connection, stored under <c>telnet</c> in settings.json.</summary>
public sealed record TelnetSettings : ISettingsSection
{
	public const int MinConnectTimeoutSeconds = 1;

	public const int MaxConnectTimeoutSeconds = 300;

	public const int MinNegotiationTimeoutSeconds = 1;

	public const int MaxNegotiationTimeoutSeconds = 60;

	public const int MinAutoLoginTimeoutSeconds = 5;

	public const int MaxAutoLoginTimeoutSeconds = 600;

	public const int MaxKeepAliveSeconds = 3600;

	public static string SectionKey => "telnet";

	/// <summary>How long to wait for the server to accept the TCP connection.</summary>
	public int ConnectTimeoutSeconds { get; init; } = 15;

	/// <summary>
	/// How long the session waits for the opening option negotiation before it shows the terminal. Devices that
	/// negotiate nothing always use it up, so keep it short.
	/// </summary>
	public int NegotiationTimeoutSeconds { get; init; } = 3;

	/// <summary>How long a connection with automatic login keeps watching the output for a login prompt.</summary>
	public int AutoLoginTimeoutSeconds { get; init; } = 30;

	/// <summary>
	/// Idle seconds before an IAC NOP goes out, which keeps firewalls and terminal servers from dropping a quiet
	/// session. 0 sends nothing.
	/// </summary>
	public int KeepAliveSeconds { get; init; }

	internal TimeSpan ConnectTimeout => TimeSpan.FromSeconds(ConnectTimeoutSeconds);

	internal TimeSpan NegotiationTimeout => TimeSpan.FromSeconds(NegotiationTimeoutSeconds);

	internal TimeSpan AutoLoginTimeout => TimeSpan.FromSeconds(AutoLoginTimeoutSeconds);

	internal TimeSpan KeepAlive => KeepAliveSeconds > 0 ? TimeSpan.FromSeconds(KeepAliveSeconds) : TimeSpan.Zero;

	/// <summary>A copy with every value inside the range the settings page allows.</summary>
	public TelnetSettings Clamped() => this with
	{
		ConnectTimeoutSeconds = Math.Clamp(ConnectTimeoutSeconds, MinConnectTimeoutSeconds, MaxConnectTimeoutSeconds),
		NegotiationTimeoutSeconds = Math.Clamp(NegotiationTimeoutSeconds, MinNegotiationTimeoutSeconds, MaxNegotiationTimeoutSeconds),
		AutoLoginTimeoutSeconds = Math.Clamp(AutoLoginTimeoutSeconds, MinAutoLoginTimeoutSeconds, MaxAutoLoginTimeoutSeconds),
		KeepAliveSeconds = Math.Clamp(KeepAliveSeconds, 0, MaxKeepAliveSeconds),
	};
}
