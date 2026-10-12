namespace Mokaterm.Modules.Ssh.Admin.Reboot;

/// <summary>Whether the machine wants a reboot to finish its updates, and what says so.</summary>
internal sealed record RebootStatus
{
	/// <summary>Nothing on the machine says either way.</summary>
	public static RebootStatus Unknown { get; } = new();

	/// <summary>True when something asks for a reboot, false when the machine's own check says none is needed, null when nothing says.</summary>
	public bool? Required { get; init; }

	/// <summary>The packages that asked for it, where the machine lists them.</summary>
	public IReadOnlyList<string> Packages { get; init; } = [];

	/// <summary>What gave the answer, such as <c>/var/run/reboot-required</c>, for the line that shows it.</summary>
	public string Source { get; init; } = "";
}
