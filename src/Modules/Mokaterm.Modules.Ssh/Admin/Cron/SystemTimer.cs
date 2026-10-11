namespace Mokaterm.Modules.Ssh.Admin.Cron;

/// <summary>A systemd timer as <c>systemctl show</c> describes it. Times stay as systemd printed them.</summary>
internal sealed record SystemTimer
{
	public required string Name { get; init; }

	public string Description { get; init; } = "";

	public string ActiveState { get; init; } = "";

	/// <summary>When it fires next, or empty when it will not.</summary>
	public string Next { get; init; } = "";

	/// <summary>When it last fired, or empty when it never has.</summary>
	public string Last { get; init; } = "";

	/// <summary>The unit it starts.</summary>
	public string Activates { get; init; } = "";

	/// <summary>Its schedule: <c>OnCalendar=</c> expressions and monotonic ones such as <c>OnBootSec=15min</c>.</summary>
	public string Schedule { get; init; } = "";
}
