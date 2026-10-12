using System.Globalization;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.Fleet;
using Mokaterm.Modules.Ssh.Admin.Detection;
using Mokaterm.Modules.Ssh.Admin.Overview;
using Mokaterm.Modules.Ssh.Admin.Packages;
using Mokaterm.Modules.Ssh.Admin.Reboot;
using Mokaterm.Modules.Ssh.Admin.Services;
using Mokaterm.Modules.Ssh.Admin.Storage;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Admin.Survey;

/// <summary>
/// The fleet overview's read of one machine: a single script that runs the system view's own scripts whole, each as a
/// part with a time limit of its own, so one that hangs (a df on a dead NFS mount, a docker daemon that never answers)
/// costs that part and not the whole read. Each part's output goes back to the parser that already reads it.
/// </summary>
/// <remarks>
/// The parts are framed with <see cref="PartPrefix"/> rather than the usual <c>@@mt:</c>, because the scripts inside
/// print <c>@@mt:</c> sections of their own and two of them use the same names. Nothing in here is slow on purpose:
/// updates come from the package manager's cache and Docker is asked without sudo.
/// </remarks>
internal static class SurveyCommands
{
	/// <summary>The line that starts each part of the survey's output, and the one after it that carries the part's exit code.</summary>
	public const string PartPrefix = "@@mt-part:";

	private const string ExitPart = "exit";

	private const string ProbePart = "probe";

	private const string OverviewPart = "overview";

	private const string UpdatesPart = "updates";

	private const string ServicesPart = "services";

	private const string RebootPart = "reboot";

	private const string DockerPart = "docker";

	private static readonly (string Name, int Seconds, string Label)[] Parts =
	[
		(ProbePart, 8, "The system details"),
		(OverviewPart, 8, "Load, memory and disks"),
		(UpdatesPart, 15, "The update check"),
		(ServicesPart, 8, "The service check"),
		(RebootPart, 8, "The reboot check"),
		(DockerPart, 8, "Docker"),
	];

	/// <summary>
	/// The whole read. Its own time limit is the sum of the parts' and a little more, so the parts give up first, and it
	/// fits the minute a host gets by default.
	/// </summary>
	public static AdminScript Script { get; } = new()
	{
		Name = "survey",
		Timeout = TimeSpan.FromSeconds(Parts.Sum(part => part.Seconds) + 5),
		OutputLimitBytes = 1024 * 1024,
		Body = string.Join('\n',
			ShellTimeout.Function,
			"mokaterm_part() {",
			$"\techo '{PartPrefix}'\"$1\"",
			"\tmokaterm_limit \"$2\" sh -c \"$3\"",
			"\tmokaterm_code=$?",

			// A part whose output does not end in a line break would otherwise swallow the marker into its last line.
			$"\tprintf '\\n{PartPrefix}{ExitPart} %s %s\\n' \"$1\" \"$mokaterm_code\"",
			"}",
			Part(ProbePart, SystemProbe.Script.Body),
			Part(OverviewPart, OverviewCommands.Read().Body),
			Part(UpdatesPart, UpdatesBody()),
			Part(ServicesPart, ServicesBody()),
			Part(RebootPart, RebootCommands.Status().Body),
			Part(DockerPart, DockerBody()),
			"exit 0"),
	};

	/// <summary>The summary in the survey's output. Parts that are missing, failed or ran out of time leave their fields empty.</summary>
	public static HostSummary Parse(string output)
	{
		AdminSections parts = AdminSections.Parse(output, PartPrefix);
		Dictionary<string, int> exits = ExitCodes(parts);
		List<string> notes = [];
		foreach ((string name, int seconds, string label) in Parts)
		{
			if (exits.TryGetValue(name, out int code) && ShellTimeout.RanOut(code))
			{
				notes.Add($"{label} did not finish within {seconds.ToString(CultureInfo.InvariantCulture)} s.");
			}
		}

		// dnf -C without a system cache, for one: the count would be a wrong zero, so it stays unknown and says why.
		if (exits.TryGetValue(UpdatesPart, out int updatesExit) && updatesExit != 0 && !ShellTimeout.RanOut(updatesExit))
		{
			notes.Add("The package manager could not list updates from its cache.");
		}

		SystemFacts facts = SystemProbe.Parse(parts.Get(ProbePart) ?? "");
		SystemProfile profile = SystemProfile.From(facts);
		OverviewSnapshot overview = OverviewCommands.Parse(parts.Get(OverviewPart) ?? "");
		Updates updates = ReadUpdates(Finished(parts, exits, UpdatesPart), notes);
		IReadOnlyList<string>? failed = ReadFailedServices(Finished(parts, exits, ServicesPart), profile);
		(int Running, int Total)? containers = ReadContainers(Finished(parts, exits, DockerPart));
		long? total = overview.MemoryTotalKb is { } totalKb and > 0 ? totalKb * 1024 : null;
		return new HostSummary
		{
			OperatingSystem = facts.KernelName.Length > 0 || facts.OsRelease.Values.Count > 0 || facts.MacProduct.Length > 0 ? profile.DisplayName : null,
			Kernel = NullIfEmpty(facts.Kernel),
			HostName = NullIfEmpty(facts.HostName),
			Uptime = overview.Uptime,
			Load1 = overview.Load1,
			Load5 = overview.Load5,
			Load15 = overview.Load15,
			CpuCount = overview.Cores,
			MemoryTotalBytes = total,
			MemoryUsedBytes = total is { } bytes && overview.MemoryAvailableKb is { } available ? Math.Max(0, bytes - (available * 1024)) : null,
			FullestDisk = Fullest(overview.Disks),
			PendingUpdates = updates.Pending,
			SecurityUpdates = updates.Security,
			RebootRequired = profile.IsContainer ? null : ReadReboot(Finished(parts, exits, RebootPart)),
			FailedServices = failed?.Count,
			FailedServiceNames = failed ?? [],
			ContainersRunning = containers?.Running,
			ContainersTotal = containers?.Total,
			Notes = notes,
		};
	}

	private static string Part(string name, string body) =>
		$"mokaterm_part {name} {Parts.Single(part => part.Name == name).Seconds.ToString(CultureInfo.InvariantCulture)} {PosixShell.Quote(body)}";

	/// <summary>
	/// The first package manager that is there, in detection's order, prints its name as a section and then what its
	/// cache says is pending. Choosing in the script is what keeps this one round trip.
	/// </summary>
	private static string UpdatesBody()
	{
		List<string> lines = [];
		string keyword = "if";
		foreach (PackageManagerKind kind in SystemProfile.PackageManagerSearchOrder)
		{
			IEnumerable<string> tests = PackageCommands.RequiredCommands(kind).Select(command => $"command -v {command} >/dev/null 2>&1");
			lines.Add($"{keyword} {string.Join(" && ", tests)}; then");
			lines.Add("\t" + AdminSections.Start(kind.ToString().ToLowerInvariant()));
			lines.AddRange(new PackageCommands(kind).ListCachedUpdates().Body.Split('\n').Select(line => "\t" + line));
			keyword = "elif";
		}

		lines.Add("fi");
		return string.Join('\n', lines);
	}

	/// <summary>Both service managers' own listings; which one counts is decided from the probe, the way the system view decides.</summary>
	private static string ServicesBody() => string.Join('\n',
		"if command -v systemctl >/dev/null 2>&1; then",
		"\t" + AdminSections.Start("units"),
		"\t" + ServiceCommands.SystemdUnits,
		"fi",
		"if command -v rc-status >/dev/null 2>&1; then",
		"\t" + AdminSections.Start("status"),
		"\t" + ServiceCommands.OpenRcStatus,
		"fi",
		"exit 0");

	/// <summary>
	/// One status line per container, the form every docker version prints, read with the rule the container list uses.
	/// A login that may not reach the daemon prints nothing, which leaves the count unknown rather than zero.
	/// </summary>
	private static string DockerBody() => string.Join('\n',
		"if command -v docker >/dev/null 2>&1; then",
		"\tif mokaterm_states=$(docker ps --all --format '{{.Status}}' 2>/dev/null); then",
		"\t\t" + AdminSections.Start("status"),
		"\t\tprintf '%s\\n' \"$mokaterm_states\"",
		"\tfi",
		"fi",
		"exit 0");

	private static Dictionary<string, int> ExitCodes(AdminSections parts)
	{
		Dictionary<string, int> exits = new(StringComparer.Ordinal);
		foreach ((string value, _) in parts.All(ExitPart))
		{
			if (value.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [var name, var code]
				&& int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out int exit))
			{
				exits[name] = exit;
			}
		}

		return exits;
	}

	/// <summary>The part's output when it ran to its end and said so, or null.</summary>
	private static string? Finished(AdminSections parts, Dictionary<string, int> exits, string name) =>
		exits.TryGetValue(name, out int code) && code == 0 ? parts.Get(name) : null;

	private static Updates ReadUpdates(string? text, List<string> notes)
	{
		if (text is null)
		{
			return default;
		}

		AdminSections sections = AdminSections.Parse(text);
		if (sections.Parts.Count == 0)
		{
			// No package manager this script knows.
			return default;
		}

		(string name, string listing) = sections.Parts[0];
		if (!Enum.TryParse(name, ignoreCase: true, out PackageManagerKind kind) || kind == PackageManagerKind.None)
		{
			notes.Add("The update check named a package manager this version does not know.");
			return default;
		}

		PackageCommands packages = new(kind);
		IReadOnlyList<PackageUpdate> updates = packages.ParseUpdates(listing);

		// Only apt marks security updates within the list it already read; dnf and yum would need a second, slow read.
		return new Updates(updates.Count, packages.SecurityUpdatesAmong(updates)?.Count);
	}

	private static IReadOnlyList<string>? ReadFailedServices(string? text, SystemProfile profile)
	{
		IReadOnlyList<ServiceUnit>? units = (text, profile.Init) switch
		{
			(null, _) => null,
			(_, InitSystem.Systemd) => ServiceListParser.ParseSystemd(text),
			(_, InitSystem.OpenRc) => ServiceListParser.ParseOpenRc(text),

			// sysvinit and BusyBox keep no failed state to count.
			_ => null,
		};

		return units is null ? null : [.. units.Where(unit => unit.State == ServiceState.Failed).Select(unit => ServiceCommands.BareName(unit.Name))];
	}

	// The system view's own reboot rule, so a machine reads the same in the fleet as on its Overview.
	private static bool? ReadReboot(string? text) => text is null ? null : RebootCommands.Parse(text).Required;

	private static (int Running, int Total)? ReadContainers(string? text)
	{
		if (text is null || AdminSections.Parse(text).Get("status") is not { } statuses)
		{
			return null;
		}

		List<string> lines = [.. AdminSections.Lines(statuses)];
		return (lines.Count(line => DockerContainerRow.StateFromStatus(line) == "running"), lines.Count);
	}

	private static HostDiskUsage? Fullest(IReadOnlyList<FileSystemUsage> disks) =>
		disks
			.OrderByDescending(disk => disk.UsedPercent)
			.ThenByDescending(disk => disk.UsedKb)
			.Select(disk => new HostDiskUsage
			{
				MountPoint = disk.MountPoint,
				TotalBytes = disk.TotalKb * 1024,
				UsedBytes = disk.UsedKb * 1024,
				UsedPercent = disk.UsedPercent,
			})
			.FirstOrDefault();

	private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

	private readonly record struct Updates(int? Pending, int? Security);
}
