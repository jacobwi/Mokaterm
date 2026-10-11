namespace Mokaterm.Modules.Ssh.Admin.Services;

/// <summary>One service as the list shows it.</summary>
internal sealed record ServiceUnit
{
	/// <summary>The name actions take: <c>nginx.service</c> for systemd, the script name for OpenRC and sysvinit.</summary>
	public required string Name { get; init; }

	public string Description { get; init; } = "";

	public ServiceState State { get; init; }

	/// <summary>What the init system printed for the state, such as <c>active (running)</c> or <c>started</c>.</summary>
	public string StateText { get; init; } = "";

	/// <summary>True when it starts at boot, false when it does not, null when the init system does not say.</summary>
	public bool? Enabled { get; init; }

	/// <summary>What the init system printed for that, such as <c>enabled</c>, <c>static</c>, <c>masked</c> or the runlevels.</summary>
	public string EnabledText { get; init; } = "";

	public bool IsRunning => State is ServiceState.Running or ServiceState.Exited or ServiceState.Changing;
}
