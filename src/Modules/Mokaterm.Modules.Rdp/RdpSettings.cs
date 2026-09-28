using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Modules.Rdp;

/// <summary>Timeouts, login retries and screen behaviour shared by every RDP connection.</summary>
public sealed record RdpSettings : ISettingsSection
{
	public const int MinTimeoutSeconds = 1;

	public const int MaxConnectTimeoutSeconds = 300;

	public const int MaxAuthenticationAttempts = 10;

	public const int MinFramesPerSecond = 5;

	public const int MaxFramesPerSecond = 60;

	public const int MinRawFrameKilobytes = 8;

	public const int MaxRawFrameKilobytes = 512;

	public static string SectionKey => "rdp";

	/// <summary>How long to wait for the connection, the TLS handshake and the login check together.</summary>
	public int ConnectTimeoutSeconds { get; init; } = 20;

	/// <summary>Password prompts, the first one included, before a rejected login is final.</summary>
	public int AuthenticationAttempts { get; init; } = 3;

	/// <summary>
	/// How often changed parts of the screen are sent. Everything the server draws in between is coalesced into
	/// one region, so a lower rate costs detail in fast animations rather than dropping anything.
	/// </summary>
	public int FramesPerSecond { get; init; } = 30;

	/// <summary>
	/// Regions up to this size travel as raw pixels; larger ones are sent as PNG. Raw is cheaper on both ends,
	/// but a full screen of it is megabytes, which a web host would push through one SignalR message.
	/// </summary>
	public int RawFrameKilobytes { get; init; } = 64;

	/// <summary>Release every key held on the server when a session's tab is hidden or the screen loses focus.</summary>
	public bool ReleaseKeysWhenInactive { get; init; } = true;

	/// <summary>A copy with every value inside the range the settings page allows.</summary>
	public RdpSettings Clamped() => this with
	{
		ConnectTimeoutSeconds = Math.Clamp(ConnectTimeoutSeconds, MinTimeoutSeconds, MaxConnectTimeoutSeconds),
		AuthenticationAttempts = Math.Clamp(AuthenticationAttempts, 1, MaxAuthenticationAttempts),
		FramesPerSecond = Math.Clamp(FramesPerSecond, MinFramesPerSecond, MaxFramesPerSecond),
		RawFrameKilobytes = Math.Clamp(RawFrameKilobytes, MinRawFrameKilobytes, MaxRawFrameKilobytes),
	};

	/// <summary>The gap between frames that <see cref="FramesPerSecond"/> asks for.</summary>
	public TimeSpan FrameInterval => TimeSpan.FromMilliseconds(1000.0 / Math.Clamp(FramesPerSecond, MinFramesPerSecond, MaxFramesPerSecond));
}
