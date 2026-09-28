namespace Mokaterm.Abstractions.Settings;

public sealed record GeneralSettings : ISettingsSection
{
	/// <summary>The <see cref="AutoReconnectDelaySeconds"/> range: a second is the shortest wait worth having.</summary>
	public const int MinAutoReconnectDelaySeconds = 1;

	/// <inheritdoc cref="MinAutoReconnectDelaySeconds"/>
	public const int MaxAutoReconnectDelaySeconds = 300;

	/// <summary>The <see cref="RecentConnectionsLimit"/> range: 0 keeps no recent list at all.</summary>
	public const int MinRecentConnectionsLimit = 0;

	/// <inheritdoc cref="MinRecentConnectionsLimit"/>
	public const int MaxRecentConnectionsLimit = 50;

	public static string SectionKey => "general";

	/// <summary>
	/// Whether closing a tab asks first while its session is still connected. Closing the desktop window does not ask:
	/// it closes every session under one deadline instead, and reopening at start brings them back.
	/// </summary>
	public bool ConfirmCloseConnectedSessions { get; init; } = true;

	public bool AutoReconnect { get; init; }

	public int AutoReconnectDelaySeconds { get; init; } = 5;

	public int RecentConnectionsLimit { get; init; } = 8;

	public SessionRestoreMode RestoreSessions { get; init; } = SessionRestoreMode.Ask;

	/// <summary>A copy with both counts inside the range the settings page offers.</summary>
	public GeneralSettings Clamped() => this with
	{
		AutoReconnectDelaySeconds = Math.Clamp(AutoReconnectDelaySeconds, MinAutoReconnectDelaySeconds, MaxAutoReconnectDelaySeconds),
		RecentConnectionsLimit = Math.Clamp(RecentConnectionsLimit, MinRecentConnectionsLimit, MaxRecentConnectionsLimit),
	};
}
