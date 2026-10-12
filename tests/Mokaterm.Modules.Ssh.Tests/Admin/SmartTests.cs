using Mokaterm.Modules.Ssh.Admin;
using Mokaterm.Modules.Ssh.Admin.Storage;

namespace Mokaterm.Modules.Ssh.Tests.Admin;

/// <summary>A disk's health from smartctl: JSON from version 7, text before it, and the disks that have none.</summary>
public sealed class SmartTests
{
	private const string AtaJson = """
		@@mt:version
		smartctl 7.4 2023-08-01 r5530 [x86_64-linux-6.8.0-45-generic] (local build)
		@@mt:json
		{
		  "json_format_version": [1, 0],
		  "smartctl": {"version": [7, 4], "svn_revision": "5530", "exit_status": 0},
		  "device": {"name": "/dev/sda", "info_name": "/dev/sda [SAT]", "type": "sat", "protocol": "ATA"},
		  "model_family": "Western Digital Red",
		  "model_name": "WDC WD40EFRX-68N32N0",
		  "serial_number": "WD-WCC7K4XX0Z9P",
		  "firmware_version": "82.00A82",
		  "user_capacity": {"blocks": 7814037168, "bytes": 4000787030016},
		  "logical_block_size": 512,
		  "rotation_rate": 5400,
		  "smart_support": {"available": true, "enabled": true},
		  "smart_status": {"passed": true},
		  "ata_smart_attributes": {"revision": 16, "table": [
		    {"id": 1, "name": "Raw_Read_Error_Rate", "value": 200, "worst": 200, "thresh": 51, "when_failed": "", "flags": {"value": 47, "string": "POSR-K ", "prefailure": true}, "raw": {"value": 0, "string": "0"}},
		    {"id": 5, "name": "Reallocated_Sector_Ct", "value": 200, "worst": 200, "thresh": 140, "when_failed": "", "flags": {"prefailure": true}, "raw": {"value": 0, "string": "0"}},
		    {"id": 9, "name": "Power_On_Hours", "value": 58, "worst": 58, "thresh": 0, "when_failed": "", "flags": {"prefailure": false}, "raw": {"value": 31245, "string": "31245"}},
		    {"id": 194, "name": "Temperature_Celsius", "value": 117, "worst": 107, "thresh": 0, "when_failed": "", "flags": {"prefailure": false}, "raw": {"value": 34, "string": "34 (Min/Max 18/45)"}},
		    {"id": 197, "name": "Current_Pending_Sector", "value": 200, "worst": 200, "thresh": 0, "when_failed": "", "flags": {"prefailure": false}, "raw": {"value": 2, "string": "2"}}
		  ]},
		  "power_on_time": {"hours": 31245},
		  "power_cycle_count": 87,
		  "temperature": {"current": 34}
		}
		@@mt:exit 0
		""";

	[Fact]
	public void Json_Ata_HealthIdentityAndAttributes()
	{
		SmartReport report = SmartCommands.Parse(AtaJson);

		Assert.True(report.Passed);
		Assert.Equal("PASSED", report.HealthText);
		Assert.Equal("ATA", report.Protocol);
		Assert.Equal("WDC WD40EFRX-68N32N0", report.Model);
		Assert.Equal("WD-WCC7K4XX0Z9P", report.Serial);
		Assert.Equal(4000787030016, report.CapacityBytes);
		Assert.Equal(5400, report.RotationRpm);
		Assert.Equal(34, report.TemperatureCelsius);
		Assert.Equal(31245, report.PowerOnHours);
		Assert.Equal(87, report.PowerCycles);
		Assert.Equal("7.4", report.Version);
		Assert.Equal(0, report.ExitStatus);
		Assert.Equal(5, report.Attributes.Count);
		Assert.True(report.Attributes[0].Prefailure);
		Assert.Equal("34 (Min/Max 18/45)", report.Attributes[3].Raw);
		Assert.Equal(34, report.Attributes[3].RawNumber);

		// Two pending sectors: the disk still passes its own test, but it is worth a look.
		SmartAttribute pending = report.Attributes.Single(attribute => attribute.Id == 197);
		Assert.True(pending.Worrying);
		Assert.False(pending.Failed);
		Assert.True(report.NeedsAttention);
		Assert.Null(report.Problem);
	}

	[Fact]
	public void Json_Nvme_HealthLog()
	{
		const string Output = """
			@@mt:version
			smartctl 7.2 2020-12-30 r5155 [x86_64-linux-5.15.0-91-generic] (local build)
			@@mt:json
			{
			  "smartctl": {"version": [7, 2], "exit_status": 0},
			  "device": {"name": "/dev/nvme0n1", "info_name": "/dev/nvme0n1", "type": "nvme", "protocol": "NVMe"},
			  "model_name": "Samsung SSD 980 PRO 1TB",
			  "serial_number": "S5GXNX0R123456",
			  "firmware_version": "5B2QGXA7",
			  "nvme_total_capacity": 1000204886016,
			  "smart_support": {"available": true, "enabled": true},
			  "smart_status": {"passed": true, "nvme": {"value": 0}},
			  "nvme_smart_health_information_log": {
			    "critical_warning": 0, "temperature": 41, "available_spare": 100, "available_spare_threshold": 10,
			    "percentage_used": 3, "data_units_read": 12345678, "data_units_written": 23456789,
			    "power_cycles": 412, "power_on_hours": 5120, "unsafe_shutdowns": 31, "media_errors": 0, "num_err_log_entries": 0
			  },
			  "temperature": {"current": 41},
			  "power_cycle_count": 412,
			  "power_on_time": {"hours": 5120}
			}
			@@mt:exit 0
			""";

		SmartReport report = SmartCommands.Parse(Output);

		Assert.True(report.Passed);
		Assert.Equal("NVMe", report.Protocol);
		Assert.Equal(1000204886016, report.CapacityBytes);
		Assert.Empty(report.Attributes);
		Assert.Contains(("Percentage Used", "3%"), report.NvmeHealth);
		Assert.Contains(("Unsafe Shutdowns", "31"), report.NvmeHealth);
		Assert.Contains(("Data Units Written", "23,456,789 units (12.01 TB)"), report.NvmeHealth);
		Assert.False(report.NvmeWarning);
		Assert.False(report.NeedsAttention);
	}

	[Fact]
	public void Json_ADiskSmartctlCannotOpen_SaysWhy()
	{
		const string Output = """
			@@mt:version
			smartctl 7.4 2023-08-01 r5530 [x86_64-linux-6.8.0-45-generic] (local build)
			@@mt:json
			{"json_format_version":[1,0],"smartctl":{"version":[7,4],"exit_status":2,"messages":[{"string":"Smartctl open device: /dev/sdb failed: No such device","severity":"error"}]}}
			@@mt:exit 2
			""";

		SmartReport report = SmartCommands.Parse(Output);

		Assert.Null(report.Passed);
		Assert.Equal(2, report.ExitStatus);
		Assert.Equal("smartctl could not read the disk: Smartctl open device: /dev/sdb failed: No such device", report.Problem);
	}

	[Fact]
	public void Text_SmartctlSix_AtaWithAFailedAttribute()
	{
		const string Output = """
			@@mt:version
			smartctl 6.6 2016-05-31 r4324 [x86_64-linux-4.15.0-112-generic] (local build)
			@@mt:text
			smartctl 6.6 2016-05-31 r4324 [x86_64-linux-4.15.0-112-generic] (local build)
			Copyright (C) 2002-16, Bruce Allen, Christian Franke, www.smartmontools.org

			=== START OF INFORMATION SECTION ===
			Model Family:     Seagate Barracuda 7200.14 (AF)
			Device Model:     ST2000DM001-1ER164
			Serial Number:    Z4Z1ABCD
			Firmware Version: CC25
			User Capacity:    2,000,398,934,016 bytes [2.00 TB]
			Sector Sizes:     512 bytes logical, 4096 bytes physical
			Rotation Rate:    7200 rpm
			SMART support is: Available - device has SMART capability.
			SMART support is: Enabled

			=== START OF READ SMART DATA SECTION ===
			SMART overall-health self-assessment test result: FAILED!
			Drive failure expected in less than 24 hours. SAVE ALL DATA.

			SMART Attributes Data Structure revision number: 10
			Vendor Specific SMART Attributes with Thresholds:
			ID# ATTRIBUTE_NAME          FLAG     VALUE WORST THRESH TYPE      UPDATED  WHEN_FAILED RAW_VALUE
			  1 Raw_Read_Error_Rate     0x000f   111   099   006    Pre-fail  Always       -       38013680
			  5 Reallocated_Sector_Ct   0x0033   005   005   010    Pre-fail  Always   FAILING_NOW 3912
			  9 Power_On_Hours          0x0032   061   061   000    Old_age   Always       -       34512
			 12 Power_Cycle_Count       0x0032   100   100   020    Old_age   Always       -       112
			194 Temperature_Celsius     0x0022   036   048   000    Old_age   Always       -       36 (0 17 0 0 0)
			197 Current_Pending_Sector  0x0012   100   100   000    Old_age   Always       -       16

			@@mt:exit 24
			""";

		SmartReport report = SmartCommands.Parse(Output);

		Assert.False(report.Passed);
		Assert.Equal("FAILED!", report.HealthText);
		Assert.Equal("ATA", report.Protocol);
		Assert.Equal("ST2000DM001-1ER164", report.Model);
		Assert.Equal(2000398934016, report.CapacityBytes);
		Assert.Equal(7200, report.RotationRpm);
		Assert.Equal(36, report.TemperatureCelsius);
		Assert.Equal(34512, report.PowerOnHours);
		Assert.Equal(112, report.PowerCycles);
		Assert.Equal(24, report.ExitStatus);
		SmartAttribute reallocated = report.Attributes.Single(attribute => attribute.Id == 5);
		Assert.True(reallocated.Failed);
		Assert.Equal("FAILING_NOW", reallocated.WhenFailed);
		Assert.Equal("36 (0 17 0 0 0)", report.Attributes.Single(attribute => attribute.Id == 194).Raw);
		Assert.True(report.NeedsAttention);
	}

	[Fact]
	public void Text_Nvme_AndASolidStateRotationRate()
	{
		const string Output = """
			@@mt:version
			smartctl 6.6 2016-05-31 r4324 [x86_64-linux-5.4.0-42-generic] (local build)
			@@mt:text
			=== START OF INFORMATION SECTION ===
			Model Number:                       Samsung SSD 970 EVO Plus 500GB
			Serial Number:                      S4EVNX0N123456
			Firmware Version:                   2B2QEXM7
			Total NVM Capacity:                 500,107,862,016 [500 GB]

			=== START OF SMART DATA SECTION ===
			SMART overall-health self-assessment test result: PASSED

			SMART/Health Information (NVMe Log 0x02)
			Critical Warning:                   0x04
			Temperature:                        38 Celsius
			Available Spare:                    100%
			Available Spare Threshold:          10%
			Percentage Used:                    1%
			Data Units Written:                 4,012,345 [2.05 TB]
			Power Cycles:                       210
			Power On Hours:                     1,234
			Unsafe Shutdowns:                   12
			Media and Data Integrity Errors:    0
			@@mt:exit 0
			""";

		SmartReport report = SmartCommands.Parse(Output);

		Assert.True(report.Passed);
		Assert.Equal("NVMe", report.Protocol);
		Assert.Equal("Samsung SSD 970 EVO Plus 500GB", report.Model);
		Assert.Equal(500107862016, report.CapacityBytes);
		Assert.Equal(38, report.TemperatureCelsius);
		Assert.Equal(1234, report.PowerOnHours);
		Assert.Equal(210, report.PowerCycles);
		Assert.Contains(("Data Units Written", "4,012,345 [2.05 TB]"), report.NvmeHealth);
		Assert.DoesNotContain(report.NvmeHealth, entry => entry.Label == "Temperature");

		// Bit 2 of the critical warning: the drive's reliability is degraded.
		Assert.True(report.NvmeWarning);
		Assert.True(report.NeedsAttention);
	}

	[Theory]
	[InlineData("SMART support is: Unavailable - device lacks SMART capability.", "The disk says it has no SMART")]
	[InlineData("SMART support is: Available - device has SMART capability.\nSMART support is: Disabled", "SMART is turned off")]
	[InlineData("/dev/sdc: Unknown USB bridge [0x152d:0x0578 (0x0209)]\nPlease specify device type with the -d option.", "smartctl could not read the disk: Please specify")]
	[InlineData("Smartctl open device: /dev/sda failed: Permission denied", "smartctl could not read the disk: Smartctl open device")]
	public void Text_WithoutHealth_SaysWhy(string text, string problem)
	{
		SmartReport report = SmartCommands.Parse("@@mt:version\nsmartctl 6.6 2016-05-31 r4324\n@@mt:text\n" + text + "\n@@mt:exit 2\n");

		Assert.Null(report.Passed);
		Assert.StartsWith(problem, report.Problem, StringComparison.Ordinal);
	}

	[Fact]
	public void Script_PicksJsonByVersion_AndRunsAsRoot()
	{
		AdminScript script = SmartCommands.Read("nvme0n1");

		Assert.True(script.AsRoot);
		Assert.Contains("mt_dev='/dev/nvme0n1'", script.Body, StringComparison.Ordinal);
		Assert.Contains("grep -Eq '^smartctl ([7-9]|[1-9][0-9])[.]'", script.Body, StringComparison.Ordinal);
		Assert.Contains("smartctl -H -A -i -j \"$mt_dev\"", script.Body, StringComparison.Ordinal);
		Assert.Throws<ArgumentException>(() => SmartCommands.Read("-a"));
	}

	[Theory]
	[InlineData("sda", "disk", "", null)]
	[InlineData("nvme0n1", "disk", "Samsung SSD 980 PRO 1TB", null)]
	[InlineData("vda", "disk", "", "virtio")]
	[InlineData("xvda", "disk", "", "Xen")]
	[InlineData("sda", "disk", "QEMU HARDDISK", "virtual disk")]
	[InlineData("sdb", "disk", "Virtual disk", "virtual disk")]
	[InlineData("sda1", "part", "", "whole disk")]
	[InlineData("dm-0", "lvm", "", "lvm device")]
	[InlineData("zram0", "disk", "", "in memory")]
	public void Unavailable_ForPartitionsAndVirtualDisks(string name, string type, string model, string? reason)
	{
		string? said = SmartCommands.Unavailable(new BlockDevice { Name = name, Type = type, Model = model });

		if (reason is null)
		{
			Assert.Null(said);
		}
		else
		{
			Assert.Contains(reason, said, StringComparison.Ordinal);
		}
	}
}
