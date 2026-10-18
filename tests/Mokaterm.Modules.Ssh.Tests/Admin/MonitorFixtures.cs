namespace Mokaterm.Modules.Ssh.Tests.Admin;

/// <summary>What the overview's live sample prints on real machines, two seconds apart.</summary>
internal static class MonitorFixtures
{
	/// <summary>Ubuntu 24.04 on a VM with Docker: a NIC, a bridge and a veth, a disk with partitions, LVM and a loop image.</summary>
	public const string UbuntuFirst = """
		@@mt:uptime
		350735.47 1386514.18
		@@mt:stat
		cpu  2255 34 2290 22625563 6290 127 456 0 0 0
		@@mt:loadavg
		0.42 0.31 0.27 2/318 48211
		@@mt:meminfo
		MemTotal:        8037888 kB
		MemFree:          912000 kB
		MemAvailable:    5122000 kB
		Buffers:          182044 kB
		Cached:          3402112 kB
		SwapTotal:       2097148 kB
		SwapFree:        1897148 kB
		@@mt:netdev
		Inter-|   Receive                                                |  Transmit
		 face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed
		    lo: 2776770   11307    0    0    0     0          0         0  2776770   11307    0    0    0     0       0          0
		  eth0: 1215645    2751    0    0    0     0          0         0  1782404    4324    0    0    0   427       0          0
		docker0:  500000     900    0    0    0     0          0         0   900000    1200    0    0    0     0       0          0
		veth3f1a2b9:  500000     900    0    0    0     0          0         0   900000    1200    0    0    0     0       0          0
		@@mt:nics
		/sys/class/net/eth0/device
		@@mt:diskstats
		   7       0 loop0 54 0 2168 30 0 0 0 0 0 52 30 0 0 0 0 0 0
		 252       0 vda 95043 25213 6158522 31864 228347 263431 9946376 263920 0 191636 315948 0 0 0 0 7066 20163
		 252       1 vda1 94867 25213 6151826 31827 228342 263431 9946368 263915 0 191596 295742 0 0 0 0 0 0
		 253       0 dm-0 90000 0 6000000 30000 220000 0 9900000 260000 0 190000 290000 0 0 0 0 0 0
		@@mt:blocks
		dm-0
		loop0
		vda
		@@mt:rootfs
		Filesystem     1024-blocks     Used Available Capacity Mounted on
		/dev/vda1         60795672 41234567  16443997      72% /
		""";

	/// <summary>The same machine two seconds later: 400 ticks over 4 CPUs, 100 of them busy and 8 waiting on I/O.</summary>
	public const string UbuntuSecond = """
		@@mt:uptime
		350737.47 1386521.90
		@@mt:stat
		cpu  2315 34 2318 22625855 6298 127 468 0 0 0
		@@mt:loadavg
		0.50 0.33 0.27 3/318 48230
		@@mt:meminfo
		MemTotal:        8037888 kB
		MemFree:          900000 kB
		MemAvailable:    5000000 kB
		SwapTotal:       2097148 kB
		SwapFree:        1897148 kB
		@@mt:netdev
		Inter-|   Receive                                                |  Transmit
		 face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed
		    lo: 2786770   11407    0    0    0     0          0         0  2786770   11407    0    0    0     0       0          0
		  eth0: 1415645    2951    0    0    0     0          0         0  1882404    4424    0    0    0   427       0          0
		docker0:  600000     990    0    0    0     0          0         0  1000000    1300    0    0    0     0       0          0
		veth3f1a2b9:  600000     990    0    0    0     0          0         0  1000000    1300    0    0    0     0       0          0
		@@mt:nics
		/sys/class/net/eth0/device
		@@mt:diskstats
		   7       0 loop0 54 0 2168 30 0 0 0 0 0 52 30 0 0 0 0 0 0
		 252       0 vda 95100 25213 6162618 31900 228400 263431 9954568 264000 0 191700 316000 0 0 0 0 7066 20163
		 252       1 vda1 94924 25213 6155922 31863 228395 263431 9954560 263995 0 191660 295800 0 0 0 0 0 0
		 253       0 dm-0 90057 0 6004096 30036 220053 0 9908192 260080 0 190064 290058 0 0 0 0 0 0
		@@mt:blocks
		dm-0
		loop0
		vda
		@@mt:rootfs
		Filesystem     1024-blocks     Used Available Capacity Mounted on
		/dev/vda1         60795672 41234601  16443963      72% /
		""";

	/// <summary>
	/// An Alpine container under Docker, BusyBox userland: its only interface is the container end of a veth, so nothing
	/// is a device, and /sys/block lists the host's disks.
	/// </summary>
	public const string AlpineContainer = """
		@@mt:uptime
		8211.04 30211.80
		@@mt:stat
		cpu  10132 0 7311 3101942 412 0 233 0 0 0
		@@mt:loadavg
		0.08 0.04 0.01 1/97 112
		@@mt:meminfo
		MemTotal:        2014560 kB
		MemFree:         1104432 kB
		MemAvailable:    1612340 kB
		Buffers:           32188 kB
		Cached:           510220 kB
		SwapTotal:             0 kB
		SwapFree:              0 kB
		@@mt:netdev
		Inter-|   Receive                                                |  Transmit
		 face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed
		    lo:       0       0    0    0    0     0          0         0        0       0    0    0    0     0       0          0
		  eth0:   18342     121    0    0    0     0          0         0     2241      30    0    0    0     0       0          0
		@@mt:nics
		@@mt:diskstats
		 259       0 nvme0n1 41211 1201 3302112 9911 82010 9011 6612010 41020 0 40120 51031 0 0 0 0 1201 211
		 259       1 nvme0n1p1 41000 1201 3300000 9900 82000 9011 6612000 41000 0 40100 51000 0 0 0 0 0 0
		@@mt:blocks
		nvme0n1
		@@mt:rootfs
		Filesystem           1024-blocks    Used Available Capacity Mounted on
		overlay                 61202244   9221112  48853352  16% /
		""";

	/// <summary>FreeBSD, which has no <c>/proc</c>: every read in the sample prints nothing but df.</summary>
	public const string FreeBsd = """
		@@mt:uptime
		@@mt:stat
		@@mt:loadavg
		@@mt:meminfo
		@@mt:netdev
		@@mt:nics
		@@mt:diskstats
		@@mt:blocks
		@@mt:rootfs
		Filesystem 1024-blocks    Used   Avail Capacity  Mounted on
		/dev/ada0p2   19279644 4105512 13631764    23%    /
		""";
}
