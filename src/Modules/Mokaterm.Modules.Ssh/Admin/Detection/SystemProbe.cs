using System.Globalization;

namespace Mokaterm.Modules.Ssh.Admin.Detection;

/// <summary>
/// One round trip that tells the system view what the server is. The script only collects facts (names, ids, which
/// commands and paths exist); what they mean is decided in <see cref="SystemProfile"/>, where fixtures can test it.
/// </summary>
internal static class SystemProbe
{
	/// <summary>Every command whose presence some page or command set asks about.</summary>
	public static readonly IReadOnlyList<string> ProbedCommands =
	[
		"systemctl", "journalctl", "timedatectl", "hostnamectl", "resolvectl",
		"openrc", "rc-service", "rc-update", "rc-status", "service", "update-rc.d", "chkconfig",
		"apt-get", "dpkg-query", "dnf", "yum", "zypper", "rpm", "pacman", "checkupdates", "apk",
		"ufw", "firewall-cmd", "nft", "iptables", "ip6tables",
		"ip", "ss", "netstat", "lsblk", "df", "ps", "kill",
		"crontab", "sudo", "getent",
		"useradd", "usermod", "userdel", "adduser", "addgroup", "deluser", "chpasswd", "passwd",
		"chronyc", "busybox", "sw_vers",
	];

	/// <summary>Every path whose existence some page or command set asks about.</summary>
	public static readonly IReadOnlyList<string> ProbedPaths =
	[
		"/run/systemd/system", "/run/openrc", "/etc/init.d", "/etc/timezone", "/etc/localtime", "/usr/share/zoneinfo",
		"/etc/crontab", "/etc/cron.d", "/var/log/journal", "/run/log/journal", "/etc/ufw/ufw.conf", "/.dockerenv",
		"/run/.containerenv", "/etc/login.defs", "/etc/rc.d/init.d",
	];

	public static AdminScript Script { get; } = new()
	{
		Name = "detect",
		Timeout = TimeSpan.FromSeconds(20),
		OutputLimitBytes = 256 * 1024,
		Body = string.Join('\n',
			AdminSections.Start("facts"),
			"printf 'kernel_name=%s\\n' \"$(uname -s 2>/dev/null)\"",
			"printf 'kernel=%s\\n' \"$(uname -srm 2>/dev/null)\"",
			"printf 'hostname=%s\\n' \"$(hostname 2>/dev/null || cat /proc/sys/kernel/hostname 2>/dev/null || uname -n 2>/dev/null)\"",
			"printf 'uid=%s\\n' \"$(id -u 2>/dev/null)\"",
			"printf 'user=%s\\n' \"$(id -un 2>/dev/null)\"",
			"printf 'pid1=%s\\n' \"$(cat /proc/1/comm 2>/dev/null)\"",
			"printf 'init_link=%s\\n' \"$(readlink /sbin/init 2>/dev/null)\"",
			"printf 'ssh=%s\\n' \"$SSH_CONNECTION\"",
			"printf 'ufw=%s\\n' \"$(sed -n 's/^ENABLED=//p' /etc/ufw/ufw.conf 2>/dev/null)\"",
			AdminSections.Start("commands"),
			$"for c in {string.Join(' ', ProbedCommands)}; do",
			"\tif command -v \"$c\" >/dev/null 2>&1; then printf '%s\\n' \"$c\"; fi",
			"done",
			AdminSections.Start("paths"),
			$"for f in {string.Join(' ', ProbedPaths)}; do",
			"\tif [ -e \"$f\" ]; then printf '%s\\n' \"$f\"; fi",
			"done",
			AdminSections.Start("units"),
			"if [ -d /run/systemd/system ]; then",
			"\tfor u in firewalld ufw nftables; do printf '%s=%s\\n' \"$u\" \"$(systemctl is-active \"$u\" 2>/dev/null)\"; done",
			"fi",
			AdminSections.Start("os-release"),
			"cat /etc/os-release 2>/dev/null || cat /usr/lib/os-release 2>/dev/null",
			AdminSections.Start("sw_vers"),
			"sw_vers 2>/dev/null",
			"exit 0"),
	};

	public static SystemFacts Parse(string output)
	{
		AdminSections sections = AdminSections.Parse(output);
		Dictionary<string, string> facts = KeyValues(sections.Get("facts"));
		(string? sshAddress, int? sshPort) = ParseSshConnection(Value(facts, "ssh"));
		return new SystemFacts
		{
			KernelName = Value(facts, "kernel_name"),
			Kernel = Value(facts, "kernel"),
			HostName = Value(facts, "hostname"),
			Uid = int.TryParse(Value(facts, "uid"), NumberStyles.None, CultureInfo.InvariantCulture, out int uid) ? uid : null,
			UserName = Value(facts, "user"),
			Pid1 = Value(facts, "pid1"),
			InitLink = Value(facts, "init_link"),
			SshServerAddress = sshAddress,
			SshServerPort = sshPort,
			UfwEnabled = Value(facts, "ufw").Trim('"', '\'').ToLowerInvariant() switch
			{
				"yes" => true,
				"no" => false,
				_ => null,
			},
			Commands = new HashSet<string>(AdminSections.Lines(sections.Get("commands")).Where(ProbedCommands.Contains), StringComparer.Ordinal),
			Paths = new HashSet<string>(AdminSections.Lines(sections.Get("paths")).Where(ProbedPaths.Contains), StringComparer.Ordinal),
			UnitStates = KeyValues(sections.Get("units")),
			OsRelease = OsRelease.Parse(sections.Get("os-release")),
			MacProduct = MacProduct(sections.Get("sw_vers")),
		};
	}

	/// <summary><c>client_ip client_port server_ip server_port</c>; the server half is what this session came in on.</summary>
	internal static (string? Address, int? Port) ParseSshConnection(string value)
	{
		string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length != 4)
		{
			return (null, null);
		}

		int? port = int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed is > 0 and <= 65535 ? parsed : null;
		return (parts[2], port);
	}

	private static Dictionary<string, string> KeyValues(string? text)
	{
		Dictionary<string, string> values = new(StringComparer.Ordinal);
		foreach (string line in AdminSections.Lines(text))
		{
			int equals = line.IndexOf('=', StringComparison.Ordinal);
			if (equals > 0)
			{
				values[line[..equals].Trim()] = line[(equals + 1)..].Trim();
			}
		}

		return values;
	}

	private static string Value(Dictionary<string, string> values, string key) => values.TryGetValue(key, out string? value) ? value : "";

	private static string MacProduct(string? swVers)
	{
		string name = "";
		string version = "";
		foreach (string line in AdminSections.Lines(swVers))
		{
			int colon = line.IndexOf(':', StringComparison.Ordinal);
			if (colon <= 0)
			{
				continue;
			}

			string key = line[..colon].Trim();
			string value = line[(colon + 1)..].Trim();
			if (key == "ProductName")
			{
				name = value;
			}
			else if (key == "ProductVersion")
			{
				version = value;
			}
		}

		return $"{name} {version}".Trim();
	}
}
