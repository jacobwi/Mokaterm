using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Modules.Vnc;

/// <summary>Timeouts, login retries and view behaviour shared by every VNC connection.</summary>
public sealed record VncSettings : ISettingsSection
{
	public const int MinTimeoutSeconds = 1;

	public const int MaxConnectTimeoutSeconds = 300;

	public const int MaxAuthenticationAttempts = 10;

	public const int MinClipboardKilobytes = 1;

	public const int MaxClipboardKilobytes = 4096;

	public static string SectionKey => "vnc";

	/// <summary>How long to wait for the server to accept the connection and finish the RFB handshake.</summary>
	public int ConnectTimeoutSeconds { get; init; } = 15;

	/// <summary>Password prompts, the first one included, before a rejected login is final.</summary>
	public int AuthenticationAttempts { get; init; } = 3;

	/// <summary>Largest clipboard text moved between the session and this machine, in kilobytes.</summary>
	public int ClipboardKilobytes { get; init; } = 64;

	/// <summary>Draw a dot where the pointer is when the server sends no cursor of its own.</summary>
	public bool ShowDotCursor { get; init; } = true;

	/// <summary>A copy with every value inside the range the settings page allows.</summary>
	public VncSettings Clamped() => this with
	{
		ConnectTimeoutSeconds = Math.Clamp(ConnectTimeoutSeconds, MinTimeoutSeconds, MaxConnectTimeoutSeconds),
		AuthenticationAttempts = Math.Clamp(AuthenticationAttempts, 1, MaxAuthenticationAttempts),
		ClipboardKilobytes = Math.Clamp(ClipboardKilobytes, MinClipboardKilobytes, MaxClipboardKilobytes),
	};
}
