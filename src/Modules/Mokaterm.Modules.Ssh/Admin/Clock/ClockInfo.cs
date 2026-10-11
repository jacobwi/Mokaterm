namespace Mokaterm.Modules.Ssh.Admin.Clock;

/// <summary>The server's clock as the time page shows it.</summary>
internal sealed record ClockInfo
{
	/// <summary>The server's own rendering of now, with its zone abbreviation and offset.</summary>
	public string LocalTime { get; init; } = "";

	/// <summary>Seconds since the epoch on the server, for comparing with this computer's clock.</summary>
	public long? Epoch { get; init; }

	/// <summary>The tz database name, such as <c>Europe/Berlin</c>, or empty when the server does not say.</summary>
	public string TimeZone { get; init; } = "";

	/// <summary>Whether time synchronisation is switched on, when the server says.</summary>
	public bool? NtpEnabled { get; init; }

	/// <summary>Whether the clock is in sync with a time server right now, when the server says.</summary>
	public bool? Synchronized { get; init; }

	/// <summary>Whether timedatectl can switch synchronisation (a time service is installed).</summary>
	public bool? CanSwitchNtp { get; init; }

	/// <summary>Where the sync state came from: timedatectl, chrony, or nothing.</summary>
	public string SyncSource { get; init; } = "";

	/// <summary>Whether the hardware clock keeps local time instead of UTC, which timedatectl warns about.</summary>
	public bool? RtcInLocalTime { get; init; }
}
