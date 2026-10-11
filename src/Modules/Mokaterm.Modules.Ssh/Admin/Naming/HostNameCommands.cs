using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Admin.Naming;

/// <summary>Reading and changing the host name: hostnamectl where systemd runs, <c>/etc/hostname</c> and hostname(1) elsewhere.</summary>
internal sealed class HostNameCommands
{
	private static readonly HashSet<string> NameKeys = new(StringComparer.Ordinal) { "Static hostname", "Transient hostname", "Pretty hostname" };

	public HostNameCommands(bool hostnamectl) => UsesHostnamectl = hostnamectl;

	public bool UsesHostnamectl { get; }

	public AdminScript Read() => new()
	{
		Name = "hostname.read",
		Body = string.Join('\n',
		[
			AdminSections.Start("current"),
			"hostname 2>/dev/null || uname -n",
			AdminSections.Start("file"),
			"cat /etc/hostname 2>/dev/null",
			.. UsesHostnamectl ? (string[])[AdminSections.Start("ctl"), "hostnamectl status 2>/dev/null"] : [],
			"exit 0",
		]),
	};

	public static HostNameInfo Parse(string output)
	{
		AdminSections sections = AdminSections.Parse(output);
		List<(string Key, string Value)> details = [];
		Dictionary<string, string> names = new(StringComparer.Ordinal);
		foreach (string line in AdminSections.Lines(sections.Get("ctl")))
		{
			int colon = line.IndexOf(':', StringComparison.Ordinal);
			if (colon <= 0)
			{
				continue;
			}

			string key = line[..colon].Trim();
			string value = line[(colon + 1)..].Trim();
			if (NameKeys.Contains(key))
			{
				names[key] = value;
			}
			else if (value.Length > 0)
			{
				details.Add((key, value));
			}
		}

		string current = AdminSections.Lines(sections.Get("current")).FirstOrDefault()?.Trim() ?? "";
		string file = AdminSections.Lines(sections.Get("file")).FirstOrDefault(line => !line.TrimStart().StartsWith('#'))?.Trim() ?? "";
		return new HostNameInfo
		{
			Current = names.TryGetValue("Transient hostname", out string? transient) ? transient : current,
			Static = names.TryGetValue("Static hostname", out string? stored) ? stored : file,
			Pretty = names.TryGetValue("Pretty hostname", out string? pretty) ? pretty : "",
			Details = details,
		};
	}

	public AdminAction Set(string name, string? sshHostName)
	{
		if (AdminNames.CheckHostName(name) is { } problem)
		{
			throw new ArgumentException(problem, nameof(name));
		}

		string quoted = PosixShell.Quote(name);
		string body = UsesHostnamectl
			? $"hostnamectl set-hostname {quoted}"
			: $"set -e\nprintf '%s\\n' {quoted} > /etc/hostname\nhostname {quoted}";

		return new AdminAction
		{
			Title = $"Rename the machine to {name}",
			Script = new AdminScript { Name = "hostname.set", Body = body, AsRoot = true, Timeout = AdminScript.ActionTimeout, OutputLimitBytes = AdminScript.ActionOutputBytes },
			Description = "Changes the name now and for the next boot. /etc/hosts is left alone: if it maps the old name, sudo may warn that it cannot resolve the new one until it is updated there too.",
			Warning = sshHostName is { Length: > 0 } connected && !string.Equals(connected, name, StringComparison.OrdinalIgnoreCase) && LooksLikeName(connected)
				? $"This session connects to \"{connected}\". If that name comes from DNS or mDNS rather than a saved address, it may stop resolving."
				: null,
			ConfirmText = "Rename",
		};
	}

	private static bool LooksLikeName(string address) => !System.Net.IPAddress.TryParse(address, out _);
}
