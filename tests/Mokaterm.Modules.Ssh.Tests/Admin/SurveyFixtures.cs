using System.Globalization;
using System.Text;
using Mokaterm.Modules.Ssh.Admin.Survey;

namespace Mokaterm.Modules.Ssh.Tests.Admin;

/// <summary>
/// What the survey prints on real machines after its output marker: each part as the system view's own script prints
/// it, framed the way the survey frames it. The probe parts are the detection fixtures.
/// </summary>
internal static class SurveyFixtures
{
	public const string UbuntuOverview = """
		@@mt:uptime
		1036810.27 4031722.10
		@@mt:loadavg
		3.20 2.10 1.75 3/318 48211
		@@mt:now
		1791704467
		@@mt:cpu
		model name	: AMD EPYC 7763 64-Core Processor
		@@mt:cores
		4
		@@mt:meminfo
		MemTotal:        8037888 kB
		MemFree:          912000 kB
		MemAvailable:    2009472 kB
		Buffers:          100000 kB
		Cached:          3000000 kB
		SwapTotal:       2097148 kB
		SwapFree:        1897148 kB
		@@mt:df
		Filesystem     1024-blocks      Used Available Capacity Mounted on
		/dev/vda1         81971444  38213332  43741728      47% /
		tmpfs               803788      1104    802684       1% /run
		/dev/loop3           65536     65536         0     100% /snap/core22/1612
		/dev/vdb         209612800 191232000  18380800      92% /srv/data
		@@mt:mounts
		/dev/vda1 / ext4 rw,relatime 0 0
		tmpfs /run tmpfs rw,nosuid,nodev 0 0
		/dev/loop3 /snap/core22/1612 squashfs ro,nodev,relatime 0 0
		/dev/vdb /srv/data xfs rw,relatime 0 0
		""";

	public const string UbuntuUpdates = """
		@@mt:apt
		Listing...
		curl/noble-updates 8.5.0-2ubuntu10.5 amd64 [upgradable from: 8.5.0-2ubuntu10.4]
		libssl3t64/noble-updates,noble-security 3.0.13-0ubuntu3.4 amd64 [upgradable from: 3.0.13-0ubuntu3.3]
		openssl/noble-updates,noble-security 3.0.13-0ubuntu3.4 amd64 [upgradable from: 3.0.13-0ubuntu3.3]
		linux-image-generic/noble-updates 6.8.0-47.47 amd64 [upgradable from: 6.8.0-45.45]
		""";

	public const string UbuntuServices = """
		@@mt:units
		apt-daily.service                     loaded    inactive dead    Daily apt download activities
		backup-agent.service                  loaded    failed   failed  Nightly backup agent
		cron.service                          loaded    active   running Regular background program processing daemon
		nginx.service                         loaded    active   running A high performance web server and a reverse proxy server
		certbot.service                       loaded    failed   failed  Certbot
		""";

	public const string UbuntuDocker = """
		@@mt:status
		Up 3 days (healthy)
		Up 3 days
		Exited (0) 2 weeks ago
		Restarting (1) 4 seconds ago
		Up 2 hours (Paused)
		""";

	/// <summary>BusyBox df, cpuinfo without a model name line on ARM, and no MemAvailable on an old kernel.</summary>
	public const string AlpineOverview = """
		@@mt:uptime
		8100.55 30000.10
		@@mt:loadavg
		0.00 0.01 0.05 1/96 2031
		@@mt:now
		1791704467
		@@mt:cpu
		Hardware	: BCM2835
		@@mt:cores
		4
		@@mt:meminfo
		MemTotal:         948304 kB
		MemFree:          512000 kB
		Buffers:           20000 kB
		Cached:           100000 kB
		SwapTotal:             0 kB
		SwapFree:              0 kB
		@@mt:df
		Filesystem           1024-blocks    Used Available Capacity Mounted on
		/dev/mmcblk0p2         7342208   1782404   5212804  25% /
		devtmpfs                 10240         0     10240   0% /dev
		shm                     474152         0    474152   0% /dev/shm
		/dev/mmcblk0p1          258095     51348    206747  20% /boot
		@@mt:mounts
		/dev/mmcblk0p2 / ext4 rw,relatime 0 0
		devtmpfs /dev devtmpfs rw,nosuid,noexec,relatime,size=10240k 0 0
		shm /dev/shm tmpfs rw,nosuid,nodev,noexec,relatime 0 0
		/dev/mmcblk0p1 /boot vfat rw,relatime 0 0
		""";

	public const string AlpineUpdates = """
		@@mt:apk
		Installed:                                Available:
		busybox-1.36.1-r28                      < 1.36.1-r29
		musl-1.2.5-r0                           < 1.2.5-r1
		""";

	public const string AlpineServices = """
		@@mt:status
		 sshd                                                              [  started  ]
		 crond                                                             [  started  ]
		 nginx                                                             [  crashed  ]
		 chronyd                                                           [  stopped  ]
		""";

	/// <summary>One part as the survey frames it: its name, what it printed, a line break and its exit code.</summary>
	public static string Part(string name, string text, int exitCode = 0) =>
		string.Create(CultureInfo.InvariantCulture, $"{SurveyCommands.PartPrefix}{name}\n{text}\n{SurveyCommands.PartPrefix}exit {name} {exitCode}\n");

	public static string Survey(params string[] parts)
	{
		StringBuilder output = new();
		foreach (string part in parts)
		{
			output.Append(part);
		}

		return output.ToString().ReplaceLineEndings("\n");
	}

	public static string Ubuntu(string reboot = "@@mt:flag\n*** System restart required ***\n") => Survey(
		Part("probe", DetectionFixtures.Ubuntu2404),
		Part("overview", UbuntuOverview),
		Part("updates", UbuntuUpdates),
		Part("services", UbuntuServices),
		Part("reboot", reboot),
		Part("docker", UbuntuDocker));
}
