namespace Mokaterm.Modules.Ssh.Admin.Detection;

/// <summary>
/// What the detection script found, before any of it is interpreted: names, ids, which commands and paths exist.
/// <see cref="SystemProfile.From"/> turns this into the capability model.
/// </summary>
internal sealed record SystemFacts
{
	/// <summary><c>uname -s</c>: Linux, Darwin, FreeBSD.</summary>
	public string KernelName { get; init; } = "";

	/// <summary><c>uname -srm</c>, such as <c>Linux 6.8.0-45-generic x86_64</c>.</summary>
	public string Kernel { get; init; } = "";

	public string HostName { get; init; } = "";

	public int? Uid { get; init; }

	public string UserName { get; init; } = "";

	/// <summary>The name of PID 1 from <c>/proc/1/comm</c>; empty where there is no /proc.</summary>
	public string Pid1 { get; init; } = "";

	/// <summary>Where <c>/sbin/init</c> points, which tells BusyBox init apart.</summary>
	public string InitLink { get; init; } = "";

	/// <summary>The server's own address for this connection, from <c>SSH_CONNECTION</c>.</summary>
	public string? SshServerAddress { get; init; }

	public int? SshServerPort { get; init; }

	public IReadOnlySet<string> Commands { get; init; } = new HashSet<string>(StringComparer.Ordinal);

	public IReadOnlySet<string> Paths { get; init; } = new HashSet<string>(StringComparer.Ordinal);

	/// <summary><c>ENABLED</c> from <c>/etc/ufw/ufw.conf</c>, readable without root.</summary>
	public bool? UfwEnabled { get; init; }

	/// <summary><c>systemctl is-active</c> for the firewall services, by unit name.</summary>
	public IReadOnlyDictionary<string, string> UnitStates { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

	public OsRelease OsRelease { get; init; } = OsRelease.Empty;

	/// <summary><c>sw_vers</c> on macOS: product name and version.</summary>
	public string MacProduct { get; init; } = "";

	public bool IsRoot => Uid == 0;

	public bool Has(string command) => Commands.Contains(command);

	public bool Exists(string path) => Paths.Contains(path);

	public bool IsActive(string unit) => UnitStates.TryGetValue(unit, out string? state) && state == "active";
}
