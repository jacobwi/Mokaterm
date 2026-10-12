using Mokaterm.Abstractions.Commands;
using Mokaterm.Modules.Ssh.Admin.Detection;

namespace Mokaterm.Modules.Ssh.Admin.Services;

/// <summary>Listing, inspecting, starting, stopping, enabling and disabling services, for the init system that runs.</summary>
internal sealed class ServiceCommands
{
	// A session that came in through sshd, or a machine whose network goes with one of these, is what a click here can cut.
	private static readonly HashSet<string> SshServices = new(StringComparer.Ordinal) { "ssh", "sshd", "dropbear", "openssh" };

	private static readonly HashSet<string> NetworkServices = new(StringComparer.Ordinal)
	{
		"networking", "network", "NetworkManager", "systemd-networkd", "wicked", "netplan", "connman", "dhcpcd",
	};

	/// <summary>Every service unit systemd has loaded, one plain line each; the survey reads the failed ones from it.</summary>
	internal const string SystemdUnits = "systemctl list-units --type=service --all --no-legend --no-pager --plain 2>/dev/null";

	/// <summary>OpenRC's services with their state, crashed ones included; the survey reads it too.</summary>
	internal const string OpenRcStatus = "rc-status --servicelist --nocolor 2>/dev/null";

	private readonly SystemFacts _facts;

	public ServiceCommands(InitSystem init, SystemFacts facts)
	{
		ArgumentNullException.ThrowIfNull(facts);
		if (init == InitSystem.None)
		{
			throw new ArgumentException("A machine without an init system has no services to manage.", nameof(init));
		}

		Init = init;
		_facts = facts;
	}

	public InitSystem Init { get; }

	/// <summary>Whether starting at boot can be switched here. BusyBox init and a sysvinit without update-rc.d or chkconfig cannot.</summary>
	public bool CanEnable => Init switch
	{
		InitSystem.Systemd or InitSystem.OpenRc => true,
		InitSystem.SysV => _facts.Has("update-rc.d") || _facts.Has("chkconfig"),
		_ => false,
	};

	/// <summary>Whether the status shows recent log lines too, which only the journal keeps per service.</summary>
	public bool StatusIncludesLog => Init == InitSystem.Systemd;

	public AdminScript List() => Init switch
	{
		InitSystem.Systemd => new AdminScript
		{
			Name = "services.list",
			Body = string.Join('\n',
				AdminSections.Start("units"),
				SystemdUnits,
				AdminSections.Start("files"),
				"systemctl list-unit-files --type=service --no-legend --no-pager 2>/dev/null",
				"exit 0"),
		},
		InitSystem.OpenRc => new AdminScript
		{
			Name = "services.list",
			Body = string.Join('\n',
				AdminSections.Start("status"),
				OpenRcStatus,
				AdminSections.Start("levels"),
				"rc-update --verbose show 2>/dev/null",
				"exit 0"),
		},
		_ => new AdminScript
		{
			Name = "services.list",
			Body = string.Join('\n',
				AdminSections.Start("scripts"),
				$"ls -1 {ScriptFolder} 2>/dev/null",
				AdminSections.Start("status"),
				_facts.Has("service") ? "service --status-all 2>&1" : ":",
				AdminSections.Start("links"),
				"ls -1 /etc/rc2.d /etc/rc3.d /etc/rc5.d /etc/rc.d/rc3.d /etc/rc.d/rc5.d 2>/dev/null",
				"exit 0"),
		},
	};

	public IReadOnlyList<ServiceUnit> ParseList(string output) => Init switch
	{
		InitSystem.Systemd => ServiceListParser.ParseSystemd(output),
		InitSystem.OpenRc => ServiceListParser.ParseOpenRc(output),
		_ => ServiceListParser.ParseSysV(output),
	};

	/// <summary>The status, and for systemd the last journal lines, as text. Reading as root shows log lines a login cannot.</summary>
	public AdminScript Status(string name, bool asRoot)
	{
		Require(name);
		string quoted = PosixShell.Quote(name);
		string body = Init switch
		{
			InitSystem.Systemd => $"systemctl status --no-pager --full --lines=40 -- {quoted} 2>&1",
			InitSystem.OpenRc => $"rc-service {quoted} status 2>&1",
			_ => $"{Runner(name)} status 2>&1",
		};

		// Status exits non-zero for a stopped service, and that is an answer, not a failure.
		return new AdminScript { Name = "services.status", Body = body + "\nexit 0", AsRoot = asRoot };
	}

	public AdminAction Act(ServiceAction action, string name)
	{
		Require(name);
		string quoted = PosixShell.Quote(name);
		string verb = action.ToString().ToLowerInvariant();
		string body = Init switch
		{
			InitSystem.Systemd => $"systemctl {verb} -- {quoted}",
			InitSystem.OpenRc => action switch
			{
				ServiceAction.Enable => $"rc-update add {quoted} default",
				ServiceAction.Disable => $"rc-update --all delete {quoted}",
				_ => $"rc-service {quoted} {verb}",
			},
			_ => action switch
			{
				ServiceAction.Enable or ServiceAction.Disable => SysVEnable(quoted, action == ServiceAction.Enable),
				_ => $"{Runner(name)} {verb}",
			},
		};

		string bare = BareName(name);
		bool ssh = SshServices.Contains(bare);
		bool network = NetworkServices.Contains(bare);
		string? warning = (action, ssh, network) switch
		{
			(ServiceAction.Stop, true, _) => "This session came in through this service. Stopping it refuses every new SSH login, this app's included, until it is started from a console.",
			(ServiceAction.Restart, true, _) => "This session came in through this service. A restart keeps open sessions on most systems, but a broken configuration leaves it stopped and refuses new logins.",
			(ServiceAction.Disable, true, _) => "This session came in through this service. Disabled, it will not start at the next boot and SSH logins will be refused.",
			(ServiceAction.Stop or ServiceAction.Restart, _, true) => "The network may go down with this service, and this session with it.",
			(ServiceAction.Disable, _, true) => "The network may not come up at the next boot without this service.",
			_ => null,
		};

		return new AdminAction
		{
			Title = $"{action} {name}",
			Script = new AdminScript { Name = "services." + verb, Body = body, AsRoot = true, Timeout = AdminScript.ActionTimeout, OutputLimitBytes = AdminScript.ActionOutputBytes },
			Description = action switch
			{
				ServiceAction.Enable => "Starts it at boot from now on. It does not start it now.",
				ServiceAction.Disable => "Stops starting it at boot. It does not stop it now.",
				_ => null,
			},
			Warning = warning,
			CutsConnection = warning is not null,
			Destructive = action is ServiceAction.Stop or ServiceAction.Disable || warning is not null,
			ConfirmText = action.ToString(),
		};
	}

	/// <summary>Only systemd has drop-in overrides and unit files to show.</summary>
	public bool HasUnitFiles => Init == InitSystem.Systemd;

	/// <summary>
	/// Where <c>systemctl edit</c> keeps a unit's override: <c>/etc/systemd/system/nginx.service.d/override.conf</c>.
	/// A name without a type is a service, as systemctl reads it.
	/// </summary>
	public static string OverridePath(string name) => OverrideFolder(name) + "/override.conf";

	/// <summary>The drop-in folder of <see cref="OverridePath"/>.</summary>
	public static string OverrideFolder(string name)
	{
		Require(name);
		string unit = name.Contains('.', StringComparison.Ordinal) ? name : name + ".service";
		return $"/etc/systemd/system/{unit}.d";
	}

	/// <summary>The unit file and every drop-in that changes it, as <c>systemctl cat</c> prints them.</summary>
	public AdminScript Cat(string name)
	{
		RequireUnitFiles();
		Require(name);

		// A unit with no file of its own (one that only exists at run time) is an answer here, not a failure.
		return new AdminScript { Name = "services.cat", Body = $"systemctl cat --no-pager -- {PosixShell.Quote(name)} 2>&1\nexit 0" };
	}

	/// <summary>Has systemd read its unit files again, which is what makes a saved override count.</summary>
	public AdminAction DaemonReload(string name)
	{
		RequireUnitFiles();
		Require(name);
		return new AdminAction
		{
			Title = "Reload systemd's unit files",
			Script = new AdminScript
			{
				Name = "services.daemon-reload",
				Body = "systemctl daemon-reload",
				AsRoot = true,
				Timeout = AdminScript.ActionTimeout,
				OutputLimitBytes = AdminScript.ActionOutputBytes,
			},
			Description = $"Reads {name}'s override, and every other changed unit file, so the next start or restart uses them. Services that run keep running as they are.",
			ConfirmText = "Reload",
		};
	}

	/// <summary>The unit's name without <c>.service</c>, as the warning lists spell it.</summary>
	internal static string BareName(string name) => name.EndsWith(".service", StringComparison.Ordinal) ? name[..^".service".Length] : name;

	private string ScriptFolder => _facts.Exists("/etc/init.d") || !_facts.Exists("/etc/rc.d/init.d") ? "/etc/init.d" : "/etc/rc.d/init.d";

	// service(8) sets a clean environment and the right working folder; the script itself is the fallback BusyBox has.
	private string Runner(string name) => _facts.Has("service") && Init == InitSystem.SysV
		? $"service {PosixShell.Quote(name)}"
		: PosixShell.Quote($"{ScriptFolder}/{name}");

	private string SysVEnable(string quoted, bool enable)
	{
		if (_facts.Has("update-rc.d"))
		{
			return enable ? $"set -e\nupdate-rc.d {quoted} defaults\nupdate-rc.d {quoted} enable" : $"update-rc.d {quoted} disable";
		}

		if (_facts.Has("chkconfig"))
		{
			return $"chkconfig {quoted} {(enable ? "on" : "off")}";
		}

		throw new InvalidOperationException("This machine has no tool that switches services on at boot.");
	}

	private void RequireUnitFiles()
	{
		if (!HasUnitFiles)
		{
			throw new InvalidOperationException("Only systemd has unit files and overrides.");
		}
	}

	private static void Require(string name)
	{
		if (AdminNames.CheckService(name) is { } problem)
		{
			throw new ArgumentException(problem, nameof(name));
		}
	}
}
