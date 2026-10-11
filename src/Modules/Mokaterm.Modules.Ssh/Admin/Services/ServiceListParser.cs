namespace Mokaterm.Modules.Ssh.Admin.Services;

/// <summary>Reads what each init system prints about its services into one list.</summary>
internal static class ServiceListParser
{
	private static readonly HashSet<string> NotServices = new(StringComparer.Ordinal)
	{
		"README", "skeleton", "functions", "rc", "rcS", "rc.local", "halt", "reboot", "single", "killprocs", "sendsigs",
		"umountfs", "umountnfs.sh", "umountroot", ".depend.boot", ".depend.start", ".depend.stop", ".legacy-bootordering",
	};

	/// <summary>
	/// <c>list-units --plain</c> lines (UNIT LOAD ACTIVE SUB DESCRIPTION) joined with <c>list-unit-files</c> lines
	/// (UNIT STATE [PRESET]). Unit files that are not loaded still get a row, so a disabled service can be started.
	/// </summary>
	public static IReadOnlyList<ServiceUnit> ParseSystemd(string output)
	{
		AdminSections sections = AdminSections.Parse(output);
		Dictionary<string, string> files = new(StringComparer.Ordinal);
		foreach (string line in AdminSections.Lines(sections.Get("files")))
		{
			string[] parts = line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length >= 2 && parts[0].EndsWith(".service", StringComparison.Ordinal))
			{
				files[parts[0]] = parts[1];
			}
		}

		Dictionary<string, ServiceUnit> units = new(StringComparer.Ordinal);
		foreach (string line in AdminSections.Lines(sections.Get("units")))
		{
			string[] parts = line.TrimStart('*', ' ', '●').Split((char[])[' ', '\t'], 5, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length < 4 || !parts[0].EndsWith(".service", StringComparison.Ordinal))
			{
				continue;
			}

			string name = parts[0];
			string load = parts[1];
			string active = parts[2];
			string sub = parts[3];
			string description = parts.Length > 4 ? parts[4].Trim() : "";
			string enabledText = files.TryGetValue(name, out string? state) ? state : load == "masked" ? "masked" : "";
			units[name] = new ServiceUnit
			{
				Name = name,
				Description = load == "not-found" ? "Not found: a unit refers to it, but there is no unit file" : description,
				State = SystemdState(active, sub),
				StateText = $"{active} ({sub})",
				Enabled = SystemdEnabled(enabledText),
				EnabledText = enabledText,
			};
		}

		foreach ((string name, string enabledText) in files)
		{
			// Templates (name@.service) only exist as instances, which list-units already showed.
			if (units.ContainsKey(name) || name.Contains("@.", StringComparison.Ordinal))
			{
				continue;
			}

			units[name] = new ServiceUnit
			{
				Name = name,
				State = ServiceState.Stopped,
				StateText = "inactive (dead)",
				Enabled = SystemdEnabled(enabledText),
				EnabledText = enabledText,
			};
		}

		return [.. units.Values.OrderBy(unit => unit.Name, StringComparer.OrdinalIgnoreCase)];
	}

	/// <summary>
	/// <c>rc-status --servicelist</c> lines (<c> sshd [ started ]</c>) joined with <c>rc-update --verbose show</c> lines
	/// (<c> sshd | default</c>, nothing after the bar when it is in no runlevel).
	/// </summary>
	public static IReadOnlyList<ServiceUnit> ParseOpenRc(string output)
	{
		AdminSections sections = AdminSections.Parse(output);
		Dictionary<string, string> states = new(StringComparer.Ordinal);
		foreach (string line in AdminSections.Lines(sections.Get("status")))
		{
			int open = line.LastIndexOf('[');
			int close = line.LastIndexOf(']');
			if (open <= 0 || close < open)
			{
				continue;
			}

			string name = line[..open].Trim();
			if (name.Length > 0 && !name.Contains(' ', StringComparison.Ordinal))
			{
				states[name] = line[(open + 1)..close].Trim();
			}
		}

		Dictionary<string, string> levels = new(StringComparer.Ordinal);
		foreach (string line in AdminSections.Lines(sections.Get("levels")))
		{
			int bar = line.IndexOf('|', StringComparison.Ordinal);
			if (bar <= 0)
			{
				continue;
			}

			string name = line[..bar].Trim();
			if (name.Length > 0)
			{
				levels[name] = string.Join(' ', line[(bar + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries));
			}
		}

		return
		[
			.. states.Keys.Union(levels.Keys, StringComparer.Ordinal)
				.Order(StringComparer.OrdinalIgnoreCase)
				.Select(name =>
				{
					string state = states.TryGetValue(name, out string? value) ? value : "stopped";
					string runlevels = levels.TryGetValue(name, out string? level) ? level : "";
					return new ServiceUnit
					{
						Name = name,
						State = OpenRcState(state),
						StateText = state,
						Enabled = levels.ContainsKey(name) ? runlevels.Length > 0 : null,
						EnabledText = runlevels,
					};
				}),
		];
	}

	/// <summary>
	/// Script names from <c>/etc/init.d</c>, states from <c>service --status-all</c> (Debian's <c>[ + ]  ssh</c> or
	/// the Red Hat <c>sshd (pid 1) is running...</c>), and boot links (<c>S20ssh</c>) from the rc folders.
	/// </summary>
	public static IReadOnlyList<ServiceUnit> ParseSysV(string output)
	{
		AdminSections sections = AdminSections.Parse(output);
		Dictionary<string, (ServiceState State, string Text)> states = new(StringComparer.Ordinal);
		foreach (string line in AdminSections.Lines(sections.Get("status")))
		{
			string trimmed = line.Trim();
			if (trimmed.StartsWith('[') && trimmed.IndexOf(']', StringComparison.Ordinal) is var close and > 0)
			{
				string mark = trimmed[1..close].Trim();
				string name = trimmed[(close + 1)..].Trim();
				states[name] = mark switch
				{
					"+" => (ServiceState.Running, "running"),
					"-" => (ServiceState.Stopped, "stopped"),
					_ => (ServiceState.Unknown, "unknown"),
				};
			}
			else if (trimmed.Split(' ', 2) is [var name, var rest])
			{
				if (rest.Contains("is running", StringComparison.Ordinal))
				{
					states[name] = (ServiceState.Running, "running");
				}
				else if (rest.Contains("stopped", StringComparison.Ordinal) || rest.Contains("not running", StringComparison.Ordinal))
				{
					states[name] = (ServiceState.Stopped, "stopped");
				}
			}
		}

		HashSet<string> enabled = new(StringComparer.Ordinal);
		foreach (string line in AdminSections.Lines(sections.Get("links")))
		{
			if (line.Length > 3 && line[0] == 'S' && char.IsAsciiDigit(line[1]) && char.IsAsciiDigit(line[2]))
			{
				enabled.Add(line[3..]);
			}
		}

		bool knowsLinks = AdminSections.Lines(sections.Get("links")).Any();
		List<ServiceUnit> units = [];
		foreach (string name in AdminSections.Lines(sections.Get("scripts")).Order(StringComparer.OrdinalIgnoreCase))
		{
			if (NotServices.Contains(name) || name.EndsWith(".dpkg-old", StringComparison.Ordinal) || AdminNames.CheckService(name) is not null)
			{
				continue;
			}

			(ServiceState state, string text) = states.TryGetValue(name, out var known) ? known : (ServiceState.Unknown, "");
			units.Add(new ServiceUnit
			{
				Name = name,
				State = state,
				StateText = text,
				Enabled = knowsLinks ? enabled.Contains(name) : null,
				EnabledText = knowsLinks ? enabled.Contains(name) ? "enabled" : "disabled" : "",
			});
		}

		return units;
	}

	internal static ServiceState SystemdState(string active, string sub) => active switch
	{
		"active" when sub is "exited" => ServiceState.Exited,
		"active" => ServiceState.Running,
		"failed" => ServiceState.Failed,
		"activating" or "deactivating" or "reloading" or "refreshing" => ServiceState.Changing,
		"inactive" => ServiceState.Stopped,
		_ => ServiceState.Unknown,
	};

	private static bool? SystemdEnabled(string state) => state switch
	{
		"enabled" or "enabled-runtime" or "alias" => true,
		"disabled" or "masked" or "masked-runtime" => false,
		_ => null,
	};

	private static ServiceState OpenRcState(string state) => state switch
	{
		"started" => ServiceState.Running,
		"stopped" => ServiceState.Stopped,
		"crashed" or "failed" => ServiceState.Failed,
		"starting" or "stopping" or "inactive" or "scheduled" => ServiceState.Changing,
		_ => ServiceState.Unknown,
	};
}
