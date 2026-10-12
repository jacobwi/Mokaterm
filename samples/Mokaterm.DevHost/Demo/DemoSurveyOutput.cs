using System.Globalization;
using System.Text;
using Mokaterm.DevHost.Demo.FileSystem;

namespace Mokaterm.DevHost.Demo;

/// <summary>
/// What the fleet overview's survey script prints on each demo machine, so the overview has something to show here:
/// web01 is an Ubuntu box waiting for a reboot, the build server is overloaded, the nas is an Alpine box on OpenRC
/// whose Docker never answers, and any other name gets a Rocky or an Arch machine. Each part is what the system view's
/// own script prints there, framed the way the survey frames it.
/// </summary>
internal static class DemoSurveyOutput
{
	/// <summary>The line the survey puts in front of each part, and in front of each part's exit code.</summary>
	private const string PartPrefix = "@@mt-part:";

	public static string For(DemoAccount account, string hostName, DateTimeOffset now)
	{
		long epoch = now.ToUnixTimeSeconds();
		return hostName switch
		{
			"web01" => Ubuntu(account, hostName, epoch),
			"build-server" => Debian(account, hostName, epoch),
			"nas" => Alpine(account, hostName, epoch),
			_ when DemoRandom.Next(DemoRandom.Seed(hostName), 2) == 0 => Rocky(account, hostName, epoch),
			_ => Arch(account, hostName, epoch),
		};
	}

	private static string Ubuntu(DemoAccount account, string hostName, long epoch) => Survey(
		Part("probe", Probe(account, hostName, "Linux 6.8.0-45-generic x86_64", systemd: true, ["systemctl", "apt-get", "dpkg-query", "docker"], """
			PRETTY_NAME="Ubuntu 24.04.1 LTS"
			NAME="Ubuntu"
			VERSION_ID="24.04"
			ID=ubuntu
			ID_LIKE=debian
			""")),
		Part("overview", Overview(epoch, uptime: 1036810, load: "0.42 0.31 0.27", cores: 4, totalKb: 8037888, availableKb: 3122000, """
			/dev/vda1         81971444  38213332  43741728      47% /
			tmpfs               803788      1104    802684       1% /run
			/dev/vdb         209612800 191232000  18380800      92% /srv/data
			""", """
			/dev/vda1 / ext4 rw,relatime 0 0
			tmpfs /run tmpfs rw,nosuid,nodev 0 0
			/dev/vdb /srv/data xfs rw,relatime 0 0
			""")),
		Part("updates", """
			@@mt:apt
			Listing...
			curl/noble-updates 8.5.0-2ubuntu10.5 amd64 [upgradable from: 8.5.0-2ubuntu10.4]
			libssl3t64/noble-updates,noble-security 3.0.13-0ubuntu3.4 amd64 [upgradable from: 3.0.13-0ubuntu3.3]
			openssl/noble-updates,noble-security 3.0.13-0ubuntu3.4 amd64 [upgradable from: 3.0.13-0ubuntu3.3]
			linux-image-generic/noble-updates 6.8.0-47.47 amd64 [upgradable from: 6.8.0-45.45]
			"""),
		Part("services", """
			@@mt:units
			backup-agent.service                  loaded    failed   failed  Nightly backup agent
			cron.service                          loaded    active   running Regular background program processing daemon
			nginx.service                         loaded    active   running A high performance web server and a reverse proxy server
			ssh.service                           loaded    active   running OpenBSD Secure Shell server
			"""),
		Part("reboot", "@@mt:flag\n*** System restart required ***\n"),
		Part("docker", """
			@@mt:status
			Up 12 days (healthy)
			Up 12 days (healthy)
			Up 12 days
			Up 3 days
			Up 3 days
			Up 40 hours
			Exited (0) 2 weeks ago
			Exited (137) 5 days ago
			"""));

	private static string Debian(DemoAccount account, string hostName, long epoch) => Survey(
		Part("probe", Probe(account, hostName, "Linux 6.1.0-25-amd64 x86_64", systemd: true, ["systemctl", "apt-get", "dpkg-query", "docker"], """
			PRETTY_NAME="Debian GNU/Linux 12 (bookworm)"
			NAME="Debian GNU/Linux"
			VERSION_ID="12"
			ID=debian
			""")),
		Part("overview", Overview(epoch, uptime: 268200, load: "7.85 6.10 5.02", cores: 4, totalKb: 16314880, availableKb: 980000, """
			/dev/nvme0n1p2   479596204 284312116 170856572      63% /
			/dev/nvme0n1p1      523248      6232    517016       2% /boot/efi
			""", """
			/dev/nvme0n1p2 / ext4 rw,relatime 0 0
			/dev/nvme0n1p1 /boot/efi vfat rw,relatime 0 0
			""")),
		Part("updates", "@@mt:apt\nListing...\n"),
		Part("services", """
			@@mt:units
			cron.service                          loaded    active   running Regular background program processing daemon
			docker.service                        loaded    active   running Docker Application Container Engine
			ssh.service                           loaded    active   running OpenBSD Secure Shell server
			"""),
		Part("reboot", "@@mt:flag-watch\n"),
		Part("docker", "@@mt:status\n" + string.Join('\n', Enumerable.Range(0, 20).Select(index => index < 14 ? $"Up {index + 1} hours" : "Exited (0) 2 days ago")) + "\n"));

	private static string Alpine(DemoAccount account, string hostName, long epoch) => Survey(
		Part("probe", Probe(account, hostName, "Linux 6.6.51-0-lts x86_64", systemd: false, ["openrc", "rc-service", "rc-update", "rc-status", "apk", "busybox", "docker"], """
			NAME="Alpine Linux"
			ID=alpine
			VERSION_ID=3.20.3
			PRETTY_NAME="Alpine Linux v3.20"
			""")),
		Part("overview", Overview(epoch, uptime: 3542400, load: "0.05 0.03 0.01", cores: 4, totalKb: 948304, availableKb: null, """
			/dev/sda3              7342208   1782404   5212804  25% /
			/dev/md0            3844640564 2690000000 1154640564  71% /mnt/pool
			devtmpfs                 10240         0     10240   0% /dev
			""", """
			/dev/sda3 / ext4 rw,relatime 0 0
			/dev/md0 /mnt/pool ext4 rw,relatime 0 0
			devtmpfs /dev devtmpfs rw,nosuid,noexec,relatime,size=10240k 0 0
			""")),
		Part("updates", """
			@@mt:apk
			Installed:                                Available:
			busybox-1.36.1-r28                      < 1.36.1-r29
			musl-1.2.5-r0                           < 1.2.5-r1
			"""),
		Part("services", """
			@@mt:status
			 sshd                                                              [  started  ]
			 crond                                                             [  started  ]
			 nginx                                                             [  crashed  ]
			 smbd                                                              [  started  ]
			"""),
		Part("reboot", "@@mt:modules-gone\n"),
		Part("docker", "", exitCode: 124));

	private static string Rocky(DemoAccount account, string hostName, long epoch) => Survey(
		Part("probe", Probe(account, hostName, "Linux 5.14.0-427.13.1.el9_4.x86_64 x86_64", systemd: true, ["systemctl", "dnf", "yum", "rpm", "needs-restarting"], """
			NAME="Rocky Linux"
			VERSION_ID="9.4"
			ID="rocky"
			ID_LIKE="rhel centos fedora"
			PRETTY_NAME="Rocky Linux 9.4 (Blue Onyx)"
			""")),
		Part("overview", Overview(epoch, uptime: 5875200, load: "0.91 0.88 0.80", cores: 2, totalKb: 3825584, availableKb: 2210000, """
			/dev/mapper/rl-root  17811456   6143212  11668244      35% /
			/dev/sda1             1038336    359476    678860      35% /boot
			""", """
			/dev/mapper/rl-root / xfs rw,relatime 0 0
			/dev/sda1 /boot xfs rw,relatime 0 0
			""")),
		Part("updates", "@@mt:dnf\n", exitCode: 1),
		Part("services", "@@mt:units\nsshd.service                          loaded    active   running OpenSSH server daemon\n"),
		Part("reboot", "@@mt:needs-restarting\nReboot should not be necessary.\n@@mt:needs-restarting-exit\n0\n"),
		Part("docker", ""));

	private static string Arch(DemoAccount account, string hostName, long epoch) => Survey(
		Part("probe", Probe(account, hostName, "Linux 6.10.10-arch1-1 x86_64", systemd: true, ["systemctl", "pacman", "docker"], """
			NAME="Arch Linux"
			PRETTY_NAME="Arch Linux"
			ID=arch
			BUILD_ID=rolling
			""")),
		Part("overview", Overview(epoch, uptime: 615, load: "1.20 0.40 0.15", cores: 8, totalKb: 32768000, availableKb: 26000000, """
			/dev/nvme0n1p2   975760264 412000000 514000000      45% /
			""", """
			/dev/nvme0n1p2 / btrfs rw,relatime 0 0
			""")),
		Part("updates", "@@mt:pacman\n" + string.Join('\n', Enumerable.Range(1, 12).Select(index => $"package{index} 1.{index}.0-1 -> 1.{index}.1-1")) + "\n"),
		Part("services", "@@mt:units\nsystemd-networkd-wait-online.service loaded failed failed Wait for Network to be Configured\n"),
		Part("reboot", "@@mt:modules-gone\n"),
		Part("docker", "@@mt:status\nUp 9 minutes\n"));

	private static string Probe(DemoAccount account, string hostName, string kernel, bool systemd, IEnumerable<string> commands, string osRelease)
	{
		StringBuilder text = new();
		text.Append("@@mt:facts\n")
			.Append("kernel_name=Linux\nkernel=").Append(kernel).Append('\n')
			.Append("hostname=").Append(hostName).Append('\n')
			.Append("uid=").Append(account.IsRoot ? "0" : "1000").Append('\n')
			.Append("user=").Append(account.Name).Append('\n')
			.Append(systemd ? "pid1=systemd\ninit_link=/lib/systemd/systemd\n" : "pid1=init\ninit_link=/bin/busybox\n")
			.Append("@@mt:commands\n");
		foreach (string command in commands)
		{
			text.Append(command).Append('\n');
		}

		text.Append("@@mt:paths\n").Append(systemd ? "/run/systemd/system\n" : "/run/openrc\n/etc/init.d\n")
			.Append("@@mt:units\n")
			.Append("@@mt:os-release\n").Append(Lines(osRelease))
			.Append("@@mt:sw_vers\n");
		return text.ToString();
	}

	private static string Overview(long epoch, long uptime, string load, int cores, long totalKb, long? availableKb, string df, string mounts)
	{
		StringBuilder text = new();
		text.Append(CultureInfo.InvariantCulture, $"@@mt:uptime\n{uptime}.42 {uptime * 3}.10\n")
			.Append(CultureInfo.InvariantCulture, $"@@mt:loadavg\n{load} 2/318 48211\n")
			.Append(CultureInfo.InvariantCulture, $"@@mt:now\n{epoch}\n")
			.Append("@@mt:cpu\nmodel name\t: AMD EPYC 7763 64-Core Processor\n")
			.Append(CultureInfo.InvariantCulture, $"@@mt:cores\n{cores}\n")
			.Append(CultureInfo.InvariantCulture, $"@@mt:meminfo\nMemTotal: {totalKb} kB\n");
		text.Append(availableKb is { } available
			? string.Create(CultureInfo.InvariantCulture, $"MemFree: {available / 2} kB\nMemAvailable: {available} kB\n")
			: string.Create(CultureInfo.InvariantCulture, $"MemFree: {totalKb / 2} kB\nBuffers: 20000 kB\nCached: 100000 kB\n"))
			.Append("@@mt:df\nFilesystem     1024-blocks      Used Available Capacity Mounted on\n").Append(Lines(df))
			.Append("@@mt:mounts\n").Append(Lines(mounts));
		return text.ToString();
	}

	private static string Part(string name, string text, int exitCode = 0) =>
		string.Create(CultureInfo.InvariantCulture, $"{PartPrefix}{name}\n{text.ReplaceLineEndings("\n")}\n{PartPrefix}exit {name} {exitCode}\n");

	private static string Survey(params string[] parts) => string.Concat(parts);

	/// <summary>A block written as a raw string, which drops its last line break, as the lines a tool prints.</summary>
	private static string Lines(string text) => text.ReplaceLineEndings("\n").TrimEnd('\n') + "\n";
}
