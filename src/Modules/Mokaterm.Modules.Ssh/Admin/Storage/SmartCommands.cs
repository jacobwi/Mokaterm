using System.Globalization;
using System.Text.Json;
using Mokaterm.Abstractions.Commands;

namespace Mokaterm.Modules.Ssh.Admin.Storage;

/// <summary>
/// A whole disk's health from smartmontools: <c>smartctl -H -A -i</c>, as JSON from version 7 on (<c>-j</c>) and as
/// text before it. It opens the raw device, so it always runs as root.
/// </summary>
internal static class SmartCommands
{
	/// <summary>smartctl wakes a sleeping disk and some USB bridges answer slowly, but a minute is plenty.</summary>
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

	/// <summary>Model names hypervisors give the disks they emulate.</summary>
	private static readonly string[] VirtualModels = ["QEMU", "VBOX", "VMware", "Virtual disk", "Virtual Disk", "Msft Virtual", "Xen", "BHYVE", "Google PersistentDisk", "Amazon Elastic Block Store"];

	/// <summary>The NVMe health log lines worth showing, by the label smartctl's text gives them.</summary>
	private static readonly HashSet<string> NvmeLabels = new(StringComparer.Ordinal)
	{
		"Critical Warning", "Available Spare", "Available Spare Threshold", "Percentage Used", "Data Units Read",
		"Data Units Written", "Unsafe Shutdowns", "Media and Data Integrity Errors", "Error Information Log Entries",
	};

	public static AdminScript Read(string device)
	{
		if (AdminNames.CheckBlockDevice(device) is { } problem)
		{
			throw new ArgumentException(problem, nameof(device));
		}

		return new AdminScript
		{
			Name = "disks.smart",
			AsRoot = true,
			Timeout = Timeout,
			OutputLimitBytes = 512 * 1024,
			Body = string.Join('\n',
				$"mt_dev={PosixShell.Quote("/dev/" + device)}",
				"mt_version=$(smartctl -V 2>/dev/null | head -n 1)",
				AdminSections.Start("version"),
				"printf '%s\\n' \"$mt_version\"",
				"if printf '%s\\n' \"$mt_version\" | grep -Eq '^smartctl ([7-9]|[1-9][0-9])[.]'; then",
				$"\t{AdminSections.Start("json")}",
				"\tsmartctl -H -A -i -j \"$mt_dev\" 2>&1",
				"else",
				$"\t{AdminSections.Start("text")}",
				"\tsmartctl -H -A -i \"$mt_dev\" 2>&1",
				"fi",
				"echo \"@@mt:exit $?\""),
		};
	}

	/// <summary>
	/// Why a device has no SMART worth asking for, or null when it may: a partition or anything else that is not a
	/// whole disk, or a disk a hypervisor emulates, whose health the host machine keeps to itself.
	/// </summary>
	public static string? Unavailable(BlockDevice device)
	{
		ArgumentNullException.ThrowIfNull(device);
		if (device.Type.Length > 0 && device.Type != "disk")
		{
			return device.Type switch
			{
				"part" => "SMART belongs to the whole disk, not to one partition. Pick the disk above it.",
				"rom" => "Optical drives report no SMART here.",
				"loop" => "A loop device is a file shown as a disk; it has no health of its own.",
				_ => $"SMART belongs to physical disks, and this is a {device.Type} device on top of one.",
			};
		}

		string name = device.Name;
		string? kind = name.StartsWith("vd", StringComparison.Ordinal) ? "a virtio disk"
			: name.StartsWith("xvd", StringComparison.Ordinal) ? "a Xen disk"
			: name.StartsWith("zram", StringComparison.Ordinal) || name.StartsWith("ram", StringComparison.Ordinal) ? "a disk in memory"
			: name.StartsWith("nbd", StringComparison.Ordinal) || name.StartsWith("rbd", StringComparison.Ordinal) ? "a network block device"
			: VirtualModels.Any(model => device.Model.Contains(model, StringComparison.OrdinalIgnoreCase)) ? $"a virtual disk ({device.Model})"
			: null;
		return kind is null ? null : $"This is {kind}: the machine it runs on keeps the real disks, and their health, to itself.";
	}

	public static SmartReport Parse(string output)
	{
		ArgumentNullException.ThrowIfNull(output);
		AdminSections sections = AdminSections.Parse(output);
		string version = (AdminSections.Lines(sections.Get("version")).FirstOrDefault() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries) is [_, var number, ..] ? number : "";
		int? exit = sections.All("exit").Select(part => int.TryParse(part.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int code) ? code : (int?)null).FirstOrDefault();
		SmartReport report = sections.Get("json") is { } json && ParseJson(json) is { } fromJson
			? fromJson
			: ParseText(sections.Get("json") ?? sections.Get("text") ?? "");
		return report with { Version = version, ExitStatus = report.ExitStatus ?? exit };
	}

	/// <summary>smartctl 7's <c>-j</c> output; null when it is not JSON (an older build that refused the flag).</summary>
	public static SmartReport? ParseJson(string text)
	{
		string trimmed = text.Trim();
		if (!trimmed.StartsWith('{'))
		{
			return null;
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(trimmed, new JsonDocumentOptions { MaxDepth = 16 });
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				return null;
			}

			List<string> messages = [];
			int? exit = null;
			if (root.TryGetProperty("smartctl", out JsonElement smartctl) && smartctl.ValueKind == JsonValueKind.Object)
			{
				exit = Int(smartctl, "exit_status");
				if (smartctl.TryGetProperty("messages", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
				{
					messages.AddRange(list.EnumerateArray().Select(message => Text(message, "string")).Where(message => message.Length > 0));
				}
			}

			JsonElement nvme = root.TryGetProperty("nvme_smart_health_information_log", out JsonElement log) && log.ValueKind == JsonValueKind.Object ? log : default;
			return new SmartReport
			{
				Passed = Child(root, "smart_status") is { } status ? Flag(status, "passed") : null,
				HealthText = Child(root, "smart_status") is { } verdict && Flag(verdict, "passed") is { } passed ? (passed ? "PASSED" : "FAILED") : "",
				Protocol = Child(root, "device") is { } device ? Text(device, "protocol") : "",
				Model = Text(root, "model_name"),
				Serial = Text(root, "serial_number"),
				Firmware = Text(root, "firmware_version"),
				CapacityBytes = Child(root, "user_capacity") is { } capacity ? Long(capacity, "bytes") : Long(root, "nvme_total_capacity"),
				RotationRpm = Int(root, "rotation_rate"),
				SmartAvailable = Child(root, "smart_support") is { } support ? Flag(support, "available") : null,
				SmartEnabled = Child(root, "smart_support") is { } enabled ? Flag(enabled, "enabled") : null,
				TemperatureCelsius = Child(root, "temperature") is { } temperature ? Int(temperature, "current") : null,
				PowerOnHours = Child(root, "power_on_time") is { } powerOn ? Long(powerOn, "hours") : null,
				PowerCycles = Long(root, "power_cycle_count"),
				Attributes = Attributes(root),
				NvmeHealth = nvme.ValueKind == JsonValueKind.Object ? NvmeFromJson(nvme) : [],
				NvmeWarning = nvme.ValueKind == JsonValueKind.Object
					&& ((Int(nvme, "critical_warning") ?? 0) != 0
						|| (Int(nvme, "available_spare") is { } spare && Int(nvme, "available_spare_threshold") is { } floor && spare <= floor)),
				Messages = messages,
				ExitStatus = exit,
			};
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>smartctl 5 and 6's text: the information section's <c>Key: value</c> lines, the verdict, then a table or the NVMe log.</summary>
	public static SmartReport ParseText(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		Dictionary<string, string> info = new(StringComparer.Ordinal);
		List<SmartAttribute> attributes = [];
		List<(string, string)> nvme = [];
		List<string> messages = [];
		bool? available = null;
		bool? enabled = null;
		bool inTable = false;
		bool inNvme = false;
		string health = "";
		foreach (string line in AdminSections.Lines(text))
		{
			string trimmed = line.Trim();
			if (trimmed.StartsWith("ID# ATTRIBUTE_NAME", StringComparison.Ordinal))
			{
				inTable = true;
				continue;
			}

			if (trimmed.StartsWith("SMART/Health Information", StringComparison.Ordinal))
			{
				inNvme = true;
				continue;
			}

			if (inTable && ParseAttributeLine(trimmed) is { } attribute)
			{
				attributes.Add(attribute);
				continue;
			}

			if (IsComplaint(trimmed))
			{
				messages.Add(trimmed);
				continue;
			}

			int colon = trimmed.IndexOf(':', StringComparison.Ordinal);
			if (colon <= 0)
			{
				continue;
			}

			string key = trimmed[..colon].Trim();
			string value = trimmed[(colon + 1)..].Trim();
			if (key.StartsWith("SMART overall-health self-assessment test result", StringComparison.Ordinal) || key == "SMART Health Status")
			{
				health = value;
			}
			else if (key == "SMART support is")
			{
				if (value.StartsWith("Available", StringComparison.Ordinal))
				{
					available = true;
				}
				else if (value.StartsWith("Unavailable", StringComparison.Ordinal))
				{
					available = false;
				}
				else if (value.StartsWith("Enabled", StringComparison.Ordinal))
				{
					enabled = true;
				}
				else if (value.StartsWith("Disabled", StringComparison.Ordinal))
				{
					enabled = false;
				}
			}
			else if (inNvme)
			{
				nvme.Add((key, value));
			}
			else
			{
				info.TryAdd(key, value);
			}
		}

		string? nvmeValue(string label) => nvme.FirstOrDefault(entry => entry.Item1 == label).Item2;
		return new SmartReport
		{
			Passed = health.Length == 0 ? null : health.StartsWith("PASSED", StringComparison.Ordinal) || health == "OK",
			HealthText = health,
			Protocol = nvme.Count > 0 || info.ContainsKey("Model Number") ? "NVMe"
				: attributes.Count > 0 || info.ContainsKey("Device Model") ? "ATA"
				: info.ContainsKey("Vendor") ? "SCSI"
				: "",
			Model = Info(info, "Device Model") ?? Info(info, "Model Number") ?? Info(info, "Product") ?? "",
			Serial = Info(info, "Serial Number") ?? Info(info, "Serial number") ?? "",
			Firmware = Info(info, "Firmware Version") ?? Info(info, "Revision") ?? "",
			CapacityBytes = Number(Info(info, "User Capacity") ?? Info(info, "Total NVM Capacity")),
			RotationRpm = Info(info, "Rotation Rate") is { } rotation
				? rotation.StartsWith("Solid State", StringComparison.Ordinal) ? 0 : (int?)Number(rotation)
				: null,
			SmartAvailable = available,
			SmartEnabled = enabled,
			TemperatureCelsius = (int?)(Number(nvmeValue("Temperature")) ?? attributes.FirstOrDefault(item => item.Id is 194 or 190)?.RawNumber),
			PowerOnHours = Number(nvmeValue("Power On Hours")) ?? attributes.FirstOrDefault(item => item.Id == 9)?.RawNumber,
			PowerCycles = Number(nvmeValue("Power Cycles")) ?? attributes.FirstOrDefault(item => item.Id == 12)?.RawNumber,
			Attributes = attributes,
			NvmeHealth = [.. nvme.Where(entry => NvmeLabels.Contains(entry.Item1))],
			NvmeWarning = (nvmeValue("Critical Warning") is { } warning && warning != "0x00" && warning != "0")
				|| (Number(nvmeValue("Available Spare")) is { } spare && Number(nvmeValue("Available Spare Threshold")) is { } floor && spare <= floor),
			Messages = messages,
		};
	}

	/// <summary>A line where smartctl says it could not do something, rather than a fact about the disk.</summary>
	private static bool IsComplaint(string line) =>
		line.Contains("Unable to detect device type", StringComparison.Ordinal)
		|| line.StartsWith("Smartctl open device", StringComparison.Ordinal)
		|| line.StartsWith("Please specify device type", StringComparison.Ordinal)
		|| line.Contains("Permission denied", StringComparison.Ordinal)
		|| line.StartsWith("Read Device Identity failed", StringComparison.Ordinal);

	/// <summary><c>  5 Reallocated_Sector_Ct   0x0033   200   200   140    Pre-fail  Always       -       0</c>.</summary>
	internal static SmartAttribute? ParseAttributeLine(string line)
	{
		string[] words = line.Split(' ', 10, StringSplitOptions.RemoveEmptyEntries);
		if (words.Length < 10
			|| !int.TryParse(words[0], NumberStyles.None, CultureInfo.InvariantCulture, out int id)
			|| !int.TryParse(words[3], NumberStyles.None, CultureInfo.InvariantCulture, out int value)
			|| !int.TryParse(words[4], NumberStyles.None, CultureInfo.InvariantCulture, out int worst)
			|| !int.TryParse(words[5], NumberStyles.None, CultureInfo.InvariantCulture, out int threshold))
		{
			return null;
		}

		return new SmartAttribute(id, words[1], value, worst, threshold, words[9].Trim(), words[8] == "-" ? "" : words[8], words[6] == "Pre-fail");
	}

	private static List<SmartAttribute> Attributes(JsonElement root)
	{
		if (Child(root, "ata_smart_attributes") is not { } table
			|| !table.TryGetProperty("table", out JsonElement rows)
			|| rows.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		List<SmartAttribute> attributes = [];
		foreach (JsonElement row in rows.EnumerateArray().Where(row => row.ValueKind == JsonValueKind.Object))
		{
			if (Int(row, "id") is not { } id)
			{
				continue;
			}

			string whenFailed = Text(row, "when_failed");
			attributes.Add(new SmartAttribute(
				id,
				Text(row, "name"),
				Int(row, "value") ?? 0,
				Int(row, "worst") ?? 0,
				Int(row, "thresh") ?? 0,
				Child(row, "raw") is { } raw ? Text(raw, "string") : "",
				whenFailed == "-" ? "" : whenFailed,
				Child(row, "flags") is { } flags && Flag(flags, "prefailure") == true));
		}

		return attributes;
	}

	private static List<(string, string)> NvmeFromJson(JsonElement log)
	{
		List<(string, string)> health = [];
		void Add(string label, string key, Func<long, string> format)
		{
			if (Long(log, key) is { } value)
			{
				health.Add((label, format(value)));
			}
		}

		Add("Critical Warning", "critical_warning", value => string.Create(CultureInfo.InvariantCulture, $"0x{value:x2}"));
		Add("Available Spare", "available_spare", Percent);
		Add("Available Spare Threshold", "available_spare_threshold", Percent);
		Add("Percentage Used", "percentage_used", Percent);

		// A data unit is a thousand 512-byte blocks, by the NVMe specification.
		Add("Data Units Read", "data_units_read", value => Count(value) + " units (" + Bytes(value * 512_000) + ")");
		Add("Data Units Written", "data_units_written", value => Count(value) + " units (" + Bytes(value * 512_000) + ")");
		Add("Unsafe Shutdowns", "unsafe_shutdowns", Count);
		Add("Media and Data Integrity Errors", "media_errors", Count);
		Add("Error Information Log Entries", "num_err_log_entries", Count);
		return health;
	}

	private static string Percent(long value) => string.Create(CultureInfo.InvariantCulture, $"{value}%");

	private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

	private static string Bytes(long bytes) => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1e12:0.##} TB");

	private static string? Info(Dictionary<string, string> info, string key) => info.TryGetValue(key, out string? value) && value.Length > 0 ? value : null;

	/// <summary>The first run of digits, with thousands separators skipped: <c>4,000,787,030,016 bytes [4.00 TB]</c>, <c>38 Celsius</c>, <c>100%</c>.</summary>
	private static long? Number(string? text)
	{
		if (text is null)
		{
			return null;
		}

		int index = 0;
		while (index < text.Length && !char.IsAsciiDigit(text[index]))
		{
			index++;
		}

		long value = 0;
		bool any = false;
		for (; index < text.Length; index++)
		{
			char character = text[index];
			if (char.IsAsciiDigit(character))
			{
				if (value > (long.MaxValue - 9) / 10)
				{
					return null;
				}

				value = (value * 10) + (character - '0');
				any = true;
			}
			else if (character != ',' || index + 1 >= text.Length || !char.IsAsciiDigit(text[index + 1]))
			{
				// Anything but a thousands separator ends the number, a decimal point included: "1.61 TB" is not 161.
				break;
			}
		}

		return any ? value : null;
	}

	private static JsonElement? Child(JsonElement element, string name) =>
		element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement child) && child.ValueKind == JsonValueKind.Object ? child : null;

	private static string Text(JsonElement element, string name) =>
		element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? (value.GetString() ?? "").Trim() : "";

	private static long? Long(JsonElement element, string name) =>
		element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) ? number : null;

	private static int? Int(JsonElement element, string name) =>
		Long(element, name) is { } number && number is >= int.MinValue and <= int.MaxValue ? (int)number : null;

	private static bool? Flag(JsonElement element, string name) =>
		element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
}
