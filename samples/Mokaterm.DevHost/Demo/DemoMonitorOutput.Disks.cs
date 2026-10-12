using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Mokaterm.DevHost.Demo.FileSystem;

namespace Mokaterm.DevHost.Demo;

internal static partial class DemoMonitorOutput
{
	private const long Mb = 1024;
	private const long Gb = 1024 * 1024;

	/// <summary>What du finds in a few folders, in KiB; a folder not listed holds nothing.</summary>
	private static readonly Dictionary<string, (string Name, long Kb)[]> Folders = new(StringComparer.Ordinal)
	{
		["/"] = [("usr", 3482 * Mb), ("var", 9310 * Mb), ("home", 1240 * Mb), ("opt", 2050 * Mb), ("root", 118 * Mb), ("etc", 8 * Mb), ("boot", 184 * Mb), ("tmp", 41 * Mb), ("srv", 4), ("snap", 902 * Mb)],
		["/var"] = [("lib", 5940 * Mb), ("log", 2150 * Mb), ("cache", 911 * Mb), ("backups", 204 * Mb), ("spool", 4 * Mb), ("tmp", 1 * Mb), ("mail", 4)],
		["/var/lib"] = [("docker", 4930 * Mb), ("postgresql", 712 * Mb), ("apt", 121 * Mb), ("dpkg", 61 * Mb), ("systemd", 2 * Mb), ("snapd", 110 * Mb)],
		["/var/log"] = [("journal", 1640 * Mb), ("nginx", 382 * Mb), ("apt", 2 * Mb), ("postgresql", 96 * Mb)],
		["/var/log/nginx"] = [],
		["/home"] = [("deploy", 310 * Mb)],
		["/opt"] = [("monitoring", 1220 * Mb), ("containerd", 830 * Mb)],
		["/usr"] = [("lib", 2210 * Mb), ("share", 690 * Mb), ("bin", 310 * Mb), ("local", 120 * Mb), ("include", 98 * Mb), ("sbin", 54 * Mb)],
		["/srv/data"] = [("backups", 121 * Gb), ("media", 49 * Gb), ("lost+found", 16)],
		["/srv/data/backups"] = [("postgres", 88 * Gb), ("app", 33 * Gb)],
	};

	/// <summary>Files directly in a folder besides its folders, in KiB.</summary>
	private static readonly Dictionary<string, long> LooseFiles = new(StringComparer.Ordinal)
	{
		["/var/log"] = 412 * Mb,
		["/var/log/nginx"] = 382 * Mb,
		["/srv/data/backups"] = 2 * Gb,
	};

	/// <summary>Folders an ordinary login cannot read, and what du says about them.</summary>
	private static readonly HashSet<string> RootOnly = new(StringComparer.Ordinal) { "/root", "/var/lib/docker", "/var/cache/ldconfig", "/var/lib/postgresql" };

	/// <summary>The large files the search can find, in bytes.</summary>
	private static readonly (string Path, long Bytes)[] BigFiles =
	[
		("/srv/data/backups/postgres/shop-2026-10-10.dump", 41L * 1024 * 1024 * 1024),
		("/srv/data/backups/postgres/shop-2026-10-09.dump", 40L * 1024 * 1024 * 1024),
		("/srv/data/media/uploads/archive-2025.tar", 19L * 1024 * 1024 * 1024),
		("/var/log/journal/3f1c0e2a9b8d4c7e/system@00061f.journal", 128L * 1024 * 1024),
		("/var/log/journal/3f1c0e2a9b8d4c7e/system.journal", 96L * 1024 * 1024),
		("/var/log/nginx/access.log", 302L * 1024 * 1024),
		("/var/lib/docker/overlay2/9c1e/diff/usr/lib/libLLVM.so.17", 118L * 1024 * 1024),
		("/var/lib/postgresql/16/main/base/16384/24611", 1024L * 1024 * 1024),
		("/opt/monitoring/data/wal/00000042", 134L * 1024 * 1024),
		("/home/deploy/releases/app-2.14.0.tar.gz", 212L * 1024 * 1024),
		("/usr/lib/x86_64-linux-gnu/libLLVM-17.so.1", 112L * 1024 * 1024),
	];

	/// <summary><c>du -x -k -d 1</c> of the folder the script names, GNU style: complaints, folders, then the total.</summary>
	private static string Usage(string command, DemoAccount account)
	{
		string folder = Assigned(command, "mt_path") ?? "/";
		StringBuilder text = new("@@mt:du\n");
		long total = LooseFiles.GetValueOrDefault(folder, 4);
		foreach ((string name, long kb) in Folders.GetValueOrDefault(folder, []))
		{
			string path = folder == "/" ? "/" + name : folder + "/" + name;
			if (!account.IsRoot && RootOnly.Contains(path))
			{
				text.Append("du: cannot read directory '").Append(path).Append("': Permission denied\n");
				text.Append("4\t").Append(path).Append('\n');
				total += 4;
				continue;
			}

			text.Append(CultureInfo.InvariantCulture, $"{kb}\t{path}\n");
			total += kb;
		}

		text.Append(CultureInfo.InvariantCulture, $"{total}\t{folder}\n");
		text.Append(account.IsRoot || !RootOnly.Any(path => path.StartsWith(folder, StringComparison.Ordinal)) ? "@@mt:exit 0\n" : "@@mt:exit 1\n");
		return text.ToString();
	}

	/// <summary>The large file search: the files over the asked size under the folder, as <c>bytes&lt;tab&gt;path</c>.</summary>
	private static string LargeFiles(string command, DemoAccount account)
	{
		string folder = Assigned(command, "mt_path") ?? "/";
		long overKb = Number(SizePattern(), command) ?? 0;
		string prefix = folder == "/" ? "/" : folder + "/";
		StringBuilder text = new("@@mt:files\n");
		foreach ((string path, long bytes) in BigFiles)
		{
			bool readable = account.IsRoot || !RootOnly.Any(hidden => path.StartsWith(hidden + "/", StringComparison.Ordinal));
			if (readable && path.StartsWith(prefix, StringComparison.Ordinal) && bytes > overKb * 1024)
			{
				text.Append(CultureInfo.InvariantCulture, $"{bytes}\t{path}\n");
			}
		}

		return text.Append("@@mt:exit 0\n").ToString();
	}

	/// <summary>smartctl 7.4 on the demo's one physical disk, a NAS drive that has started to collect pending sectors.</summary>
	private static string Smart(string command, DateTimeOffset now)
	{
		string device = Assigned(command, "mt_dev") ?? "";
		if (device != "/dev/sda")
		{
			return $$$"""
				@@mt:version
				smartctl 7.4 2023-08-01 r5530 [x86_64-linux-6.8.0-45-generic] (local build)
				@@mt:json
				{"json_format_version":[1,0],"smartctl":{"version":[7,4],"exit_status":1,"messages":[{"string":"{{{device}}}: Unable to detect device type","severity":"error"}]}}
				@@mt:exit 1
				""";
		}

		int temperature = 33 + (int)(Seconds(now) / 600 % 4);
		long hours = 31_245 + (long)(Seconds(now) / 3600);
		return string.Create(CultureInfo.InvariantCulture, $$$"""
			@@mt:version
			smartctl 7.4 2023-08-01 r5530 [x86_64-linux-6.8.0-45-generic] (local build)
			@@mt:json
			{
			  "json_format_version": [1, 0],
			  "smartctl": {"version": [7, 4], "argv": ["smartctl", "-H", "-A", "-i", "-j", "/dev/sda"], "exit_status": 0},
			  "device": {"name": "/dev/sda", "info_name": "/dev/sda [SAT]", "type": "sat", "protocol": "ATA"},
			  "model_family": "Western Digital Red",
			  "model_name": "WDC WD40EFRX-68N32N0",
			  "serial_number": "WD-WCC7K4XX0Z9P",
			  "firmware_version": "82.00A82",
			  "user_capacity": {"blocks": 7814037168, "bytes": 4000787030016},
			  "rotation_rate": 5400,
			  "smart_support": {"available": true, "enabled": true},
			  "smart_status": {"passed": true},
			  "ata_smart_attributes": {"revision": 16, "table": [
			    {"id": 1, "name": "Raw_Read_Error_Rate", "value": 200, "worst": 200, "thresh": 51, "when_failed": "", "flags": {"prefailure": true}, "raw": {"value": 0, "string": "0"}},
			    {"id": 3, "name": "Spin_Up_Time", "value": 180, "worst": 178, "thresh": 21, "when_failed": "", "flags": {"prefailure": true}, "raw": {"value": 7958, "string": "7958"}},
			    {"id": 4, "name": "Start_Stop_Count", "value": 100, "worst": 100, "thresh": 0, "when_failed": "", "flags": {"prefailure": false}, "raw": {"value": 87, "string": "87"}},
			    {"id": 5, "name": "Reallocated_Sector_Ct", "value": 200, "worst": 200, "thresh": 140, "when_failed": "", "flags": {"prefailure": true}, "raw": {"value": 0, "string": "0"}},
			    {"id": 9, "name": "Power_On_Hours", "value": 58, "worst": 58, "thresh": 0, "when_failed": "", "flags": {"prefailure": false}, "raw": {"value": {{{hours}}}, "string": "{{{hours}}}"}},
			    {"id": 12, "name": "Power_Cycle_Count", "value": 100, "worst": 100, "thresh": 0, "when_failed": "", "flags": {"prefailure": false}, "raw": {"value": 87, "string": "87"}},
			    {"id": 194, "name": "Temperature_Celsius", "value": 117, "worst": 107, "thresh": 0, "when_failed": "", "flags": {"prefailure": false}, "raw": {"value": {{{temperature}}}, "string": "{{{temperature}}}"}},
			    {"id": 197, "name": "Current_Pending_Sector", "value": 200, "worst": 200, "thresh": 0, "when_failed": "", "flags": {"prefailure": false}, "raw": {"value": 2, "string": "2"}},
			    {"id": 198, "name": "Offline_Uncorrectable", "value": 100, "worst": 253, "thresh": 0, "when_failed": "", "flags": {"prefailure": false}, "raw": {"value": 0, "string": "0"}},
			    {"id": 199, "name": "UDMA_CRC_Error_Count", "value": 200, "worst": 200, "thresh": 0, "when_failed": "", "flags": {"prefailure": false}, "raw": {"value": 0, "string": "0"}}
			  ]},
			  "power_on_time": {"hours": {{{hours}}}},
			  "power_cycle_count": 87,
			  "temperature": {"current": {{{temperature}}}}
			}
			@@mt:exit 0
			""");
	}

	[GeneratedRegex(@"-size \+([0-9]+)k", RegexOptions.CultureInvariant)]
	private static partial Regex SizePattern();
}
