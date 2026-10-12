using Mokaterm.Abstractions.Commands;

namespace Mokaterm.Modules.Ssh.Admin.Network;

/// <summary>The network page reads interfaces, routes, name servers and listening ports, and changes nothing but a link's state.</summary>
internal static class NetworkCommands
{
	/// <summary>
	/// One script for the whole page. <c>ip -j</c> is tried first and plain <c>ip</c> output is the fallback, because
	/// BusyBox and iproute2 older than 4.13 print no JSON.
	/// </summary>
	public static AdminScript Read(bool asRoot) => new()
	{
		Name = "network.read",
		AsRoot = asRoot,
		Body = string.Join('\n',
			"if command -v ip >/dev/null 2>&1; then",
			$"\t{AdminSections.Start("addr-json")}",
			$"\tip -j addr show 2>/dev/null || {{ {AdminSections.Start("addr-text")}; ip addr show 2>/dev/null; }}",
			$"\t{AdminSections.Start("route4-json")}",
			$"\tif ip -j route show 2>/dev/null; then {AdminSections.Start("route6-json")}; ip -j -6 route show 2>/dev/null; else {AdminSections.Start("route-text")}; ip route show 2>/dev/null; ip -6 route show 2>/dev/null; fi",
			"fi",
			"if command -v resolvectl >/dev/null 2>&1 && [ -d /run/systemd/system ]; then",
			$"\t{AdminSections.Start("resolvectl")}",
			"\tresolvectl dns 2>/dev/null",
			$"\t{AdminSections.Start("resolvectl-domain")}",
			"\tresolvectl domain 2>/dev/null",
			"fi",
			AdminSections.Start("resolv"),
			"cat /etc/resolv.conf 2>/dev/null",
			ListeningPorts,
			"exit 0"),
	};

	/// <summary>
	/// The part of a script that lists listening ports, with <c>netstat</c> where there is no <c>ss</c>; the processes
	/// behind other users' ports only show as root. <see cref="NetworkParser.ParseListening"/> reads it back.
	/// </summary>
	public static string ListeningPorts { get; } = string.Join('\n',
		"if command -v ss >/dev/null 2>&1; then",
		$"\t{AdminSections.Start("ss")}",
		"\tss -tulpn 2>/dev/null",
		"elif command -v netstat >/dev/null 2>&1; then",
		$"\t{AdminSections.Start("netstat")}",
		"\tnetstat -tulpn 2>/dev/null",
		"fi");

	/// <summary>
	/// Brings a link up or down. Taking down the interface this session arrives on, or the one the default route
	/// leaves by, is the classic way to lose a remote machine, so both say so in the confirmation.
	/// </summary>
	public static AdminAction SetLink(NetInterface networkInterface, bool up, NetworkSnapshot snapshot, string? sshServerAddress)
	{
		ArgumentNullException.ThrowIfNull(networkInterface);
		ArgumentNullException.ThrowIfNull(snapshot);
		if (AdminNames.CheckInterface(networkInterface.Name) is { } problem)
		{
			throw new ArgumentException(problem, nameof(networkInterface));
		}

		string name = networkInterface.Name;
		bool carriesSession = NetworkParser.Holds(networkInterface, sshServerAddress);
		bool carriesDefault = snapshot.Routes.Any(route => route.IsDefault && route.Device == name);
		string? warning = up ? null : (carriesSession, carriesDefault, sshServerAddress) switch
		{
			(true, _, _) => $"This SSH session arrives on {name} ({sshServerAddress}). Bringing it down cuts this session, and getting back in needs a console or another network path.",
			(_, true, _) => $"The default route leaves through {name}. Bringing it down can cut this session and every other connection that is not on a local network.",
			(_, _, null) => "The server did not say which address this session arrived on, so this view cannot tell whether bringing the interface down cuts it.",
			_ => null,
		};

		return new AdminAction
		{
			Title = up ? $"Bring {name} up" : $"Bring {name} down",
			Script = new AdminScript
			{
				Name = up ? "network.up" : "network.down",
				Body = $"ip link set dev {PosixShell.Quote(name)} {(up ? "up" : "down")}",
				AsRoot = true,
				Timeout = AdminScript.ActionTimeout,
				OutputLimitBytes = AdminScript.ActionOutputBytes,
			},
			Description = up
				? "Sets the interface administratively up. Addresses a DHCP client or a network manager would add come only if that tool notices."
				: "Sets the interface administratively down until it is brought up again or the machine restarts.",
			Warning = warning,
			CutsConnection = warning is not null,
			Destructive = !up,
			ConfirmText = up ? "Bring up" : carriesSession ? "Bring down anyway" : "Bring down",
		};
	}
}
