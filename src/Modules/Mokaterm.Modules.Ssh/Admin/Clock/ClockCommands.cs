using System.Globalization;
using Mokaterm.Abstractions.Commands;

namespace Mokaterm.Modules.Ssh.Admin.Clock;

/// <summary>
/// The time page's commands: timedatectl where systemd runs, the classic <c>/etc/localtime</c> link and
/// <c>/etc/timezone</c> file elsewhere.
/// </summary>
internal sealed class ClockCommands
{
	private const string ZoneFolder = "/usr/share/zoneinfo";

	public ClockCommands(bool timedatectl) => UsesTimedatectl = timedatectl;

	public bool UsesTimedatectl { get; }

	/// <summary>Synchronisation can only be switched through timedatectl; elsewhere it is the chrony or ntpd service's business.</summary>
	public bool CanSwitchNtp => UsesTimedatectl;

	public AdminScript Read() => new()
	{
		Name = "time.read",
		Body = string.Join('\n',
		[
			AdminSections.Start("date"),
			"date '+%Y-%m-%d %H:%M:%S %Z (%z)'",
			AdminSections.Start("epoch"),
			"date +%s",
			.. UsesTimedatectl
				? (string[])[AdminSections.Start("show"), "timedatectl show 2>/dev/null", AdminSections.Start("status"), "timedatectl status 2>/dev/null"]
				: [],
			AdminSections.Start("tzfile"),
			"cat /etc/timezone 2>/dev/null",
			AdminSections.Start("localtime"),
			"readlink /etc/localtime 2>/dev/null",
			"if command -v chronyc >/dev/null 2>&1; then",
			$"\t{AdminSections.Start("chrony")}",
			"\tchronyc -n tracking 2>/dev/null",
			"fi",
			"exit 0",
		]),
	};

	public static ClockInfo Parse(string output)
	{
		AdminSections sections = AdminSections.Parse(output);
		Dictionary<string, string> show = KeyValues(sections.Get("show"), '=');
		Dictionary<string, string> status = KeyValues(sections.Get("status"), ':');
		string zone = Get(show, "Timezone");
		if (zone.Length == 0 && Get(status, "Time zone") is { Length: > 0 } statusZone)
		{
			// "Europe/Berlin (CEST, +0200)"
			zone = statusZone.Split(' ', 2)[0];
		}

		if (zone.Length == 0)
		{
			zone = AdminSections.Lines(sections.Get("tzfile")).FirstOrDefault()?.Trim() ?? "";
		}

		if (zone.Length == 0 && AdminSections.Lines(sections.Get("localtime")).FirstOrDefault() is { } link)
		{
			int index = link.IndexOf("zoneinfo/", StringComparison.Ordinal);
			zone = index < 0 ? "" : link[(index + "zoneinfo/".Length)..];
		}

		bool? synchronized = Bool(Get(show, "NTPSynchronized")) ?? Bool(Get(status, "System clock synchronized")) ?? Bool(Get(status, "NTP synchronized"));
		bool? enabled = Bool(Get(show, "NTP")) ?? ServiceState(Get(status, "NTP service")) ?? Bool(Get(status, "Network time on"));
		string source = synchronized is null && enabled is null ? "" : "timedatectl";
		if (synchronized is null && Chrony(sections.Get("chrony")) is { } chrony)
		{
			synchronized = chrony;
			source = "chrony";
		}

		return new ClockInfo
		{
			LocalTime = AdminSections.Lines(sections.Get("date")).FirstOrDefault()?.Trim() ?? "",
			Epoch = long.TryParse(AdminSections.Lines(sections.Get("epoch")).FirstOrDefault(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long epoch) ? epoch : null,
			TimeZone = zone,
			NtpEnabled = enabled,
			Synchronized = synchronized,
			CanSwitchNtp = Bool(Get(show, "CanNTP")),
			SyncSource = source,
			RtcInLocalTime = Bool(Get(show, "LocalRTC")) ?? Bool(Get(status, "RTC in local TZ")),
		};
	}

	/// <summary>Every zone name the server knows, from timedatectl or the zoneinfo folder.</summary>
	public AdminScript ListZones() => new()
	{
		Name = "time.zones",
		Body = string.Join('\n',
		[
			.. UsesTimedatectl ? (string[])["if timedatectl list-timezones 2>/dev/null; then exit 0; fi"] : [],
			$"cd {ZoneFolder} 2>/dev/null || exit 0",
			"find . -type f 2>/dev/null | sed 's|^\\./||' | sort",
		]),
	};

	/// <summary>
	/// Zone names from either listing. The folder also holds tables, leap second files and the posix/ and right/
	/// copies; real zone names start with a capital and pass <see cref="AdminNames.CheckTimeZone"/>.
	/// </summary>
	public static IReadOnlyList<string> ParseZones(string output) =>
		[
			.. AdminSections.Lines(output)
				.Select(line => line.Trim())
				.Where(zone => zone.Length > 0 && char.IsAsciiLetterUpper(zone[0]) && AdminNames.CheckTimeZone(zone) is null
					&& !zone.StartsWith("SystemV/", StringComparison.Ordinal))
				.Distinct(StringComparer.Ordinal)
				.Order(StringComparer.Ordinal),
		];

	public AdminAction SetTimeZone(string zone)
	{
		if (AdminNames.CheckTimeZone(zone) is { } problem)
		{
			throw new ArgumentException(problem, nameof(zone));
		}

		string quoted = PosixShell.Quote(zone);
		string file = PosixShell.Quote($"{ZoneFolder}/{zone}");
		string body = UsesTimedatectl
			? $"timedatectl set-timezone {quoted}"
			: string.Join('\n',
				$"if [ ! -f {file} ]; then echo 'This machine has no such time zone.' >&2; exit 1; fi",
				$"ln -sf {file} /etc/localtime",
				$"if [ -f /etc/timezone ]; then printf '%s\\n' {quoted} > /etc/timezone; fi");

		return new AdminAction
		{
			Title = $"Set the time zone to {zone}",
			Script = new AdminScript { Name = "time.zone", Body = body, AsRoot = true, Timeout = AdminScript.ActionTimeout, OutputLimitBytes = AdminScript.ActionOutputBytes },
			Description = "Changes how the server shows local time. Programs that read the zone at start keep the old one until they restart.",
			ConfirmText = "Set time zone",
		};
	}

	public AdminAction SetNtp(bool enabled)
	{
		if (!CanSwitchNtp)
		{
			throw new InvalidOperationException("Without timedatectl, synchronisation belongs to the chrony or ntpd service.");
		}

		return new AdminAction
		{
			Title = enabled ? "Turn on time synchronisation" : "Turn off time synchronisation",
			Script = new AdminScript
			{
				Name = "time.ntp",
				Body = $"timedatectl set-ntp {(enabled ? "true" : "false")}",
				AsRoot = true,
				Timeout = AdminScript.ActionTimeout,
				OutputLimitBytes = AdminScript.ActionOutputBytes,
			},
			Description = enabled
				? "Starts the time service timedatectl manages, so the clock follows a time server."
				: "Stops the time service. The clock drifts from then on, which can break TLS, Kerberos and log ordering.",
			Destructive = !enabled,
			ConfirmText = enabled ? "Turn on" : "Turn off",
		};
	}

	private static Dictionary<string, string> KeyValues(string? text, char separator)
	{
		Dictionary<string, string> values = new(StringComparer.Ordinal);
		foreach (string line in AdminSections.Lines(text))
		{
			int index = line.IndexOf(separator, StringComparison.Ordinal);
			if (index > 0)
			{
				values[line[..index].Trim()] = line[(index + 1)..].Trim();
			}
		}

		return values;
	}

	private static string Get(Dictionary<string, string> values, string key) => values.TryGetValue(key, out string? value) ? value : "";

	private static bool? Bool(string value) => value switch
	{
		"yes" or "true" => true,
		"no" or "false" => false,
		_ => null,
	};

	private static bool? ServiceState(string value) => value switch
	{
		"active" => true,
		"inactive" => false,
		_ => null,
	};

	/// <summary>chrony's tracking report: a leap status of "Normal" means it is in sync, "Not synchronised" that it is not.</summary>
	private static bool? Chrony(string? text)
	{
		foreach (string line in AdminSections.Lines(text))
		{
			if (line.StartsWith("Leap status", StringComparison.Ordinal))
			{
				return line.EndsWith("Normal", StringComparison.Ordinal);
			}
		}

		return null;
	}
}
