namespace Mokaterm.Modules.Ssh.Tests.Admin;

/// <summary>
/// What the detection script prints on real systems of each family, after its output marker. The command and path
/// lists are what <c>command -v</c> and <c>[ -e ]</c> find there for an ordinary login with the sbin folders added.
/// </summary>
internal static class DetectionFixtures
{
	public const string Debian12 = """
		@@mt:facts
		kernel_name=Linux
		kernel=Linux 6.1.0-25-amd64 x86_64
		hostname=web1
		uid=1000
		user=alice
		pid1=systemd
		init_link=/lib/systemd/systemd
		ssh=203.0.113.9 51234 10.0.0.5 22
		ufw=no
		@@mt:commands
		systemctl
		journalctl
		timedatectl
		hostnamectl
		service
		update-rc.d
		apt-get
		dpkg-query
		ufw
		nft
		iptables
		ip6tables
		ip
		ss
		lsblk
		df
		ps
		kill
		crontab
		sudo
		getent
		useradd
		usermod
		userdel
		adduser
		addgroup
		deluser
		chpasswd
		passwd
		@@mt:paths
		/run/systemd/system
		/etc/init.d
		/etc/timezone
		/etc/localtime
		/usr/share/zoneinfo
		/etc/crontab
		/etc/cron.d
		/var/log/journal
		/etc/ufw/ufw.conf
		/etc/login.defs
		@@mt:units
		firewalld=inactive
		ufw=inactive
		nftables=inactive
		@@mt:os-release
		PRETTY_NAME="Debian GNU/Linux 12 (bookworm)"
		NAME="Debian GNU/Linux"
		VERSION_ID="12"
		VERSION="12 (bookworm)"
		VERSION_CODENAME=bookworm
		ID=debian
		HOME_URL="https://www.debian.org/"
		@@mt:sw_vers

		""";

	public const string Ubuntu2404 = """
		@@mt:facts
		kernel_name=Linux
		kernel=Linux 6.8.0-45-generic x86_64
		hostname=app-01
		uid=0
		user=root
		pid1=systemd
		init_link=/lib/systemd/systemd
		ssh=::ffff:198.51.100.7 60022 ::ffff:10.1.2.3 2222
		ufw=yes
		@@mt:commands
		systemctl
		journalctl
		timedatectl
		hostnamectl
		resolvectl
		service
		update-rc.d
		apt-get
		dpkg-query
		ufw
		nft
		iptables
		ip
		ss
		lsblk
		df
		ps
		kill
		crontab
		getent
		useradd
		usermod
		userdel
		chpasswd
		passwd
		@@mt:paths
		/run/systemd/system
		/etc/init.d
		/etc/timezone
		/etc/localtime
		/usr/share/zoneinfo
		/etc/crontab
		/etc/cron.d
		/var/log/journal
		/etc/ufw/ufw.conf
		/etc/login.defs
		@@mt:units
		firewalld=inactive
		ufw=active
		nftables=inactive
		@@mt:os-release
		PRETTY_NAME="Ubuntu 24.04.1 LTS"
		NAME="Ubuntu"
		VERSION_ID="24.04"
		ID=ubuntu
		ID_LIKE=debian
		@@mt:sw_vers

		""";

	public const string Rocky9 = """
		@@mt:facts
		kernel_name=Linux
		kernel=Linux 5.14.0-427.13.1.el9_4.x86_64 x86_64
		hostname=db1.example.com
		uid=1001
		user=ops
		pid1=systemd
		init_link=../lib/systemd/systemd
		ssh=192.0.2.10 40000 192.0.2.20 22
		ufw=
		@@mt:commands
		systemctl
		journalctl
		timedatectl
		hostnamectl
		dnf
		yum
		rpm
		firewall-cmd
		nft
		iptables
		ip
		ss
		lsblk
		df
		ps
		kill
		crontab
		sudo
		getent
		useradd
		usermod
		userdel
		chpasswd
		passwd
		chronyc
		@@mt:paths
		/run/systemd/system
		/etc/localtime
		/usr/share/zoneinfo
		/etc/crontab
		/etc/cron.d
		/run/log/journal
		/etc/login.defs
		/etc/rc.d/init.d
		@@mt:units
		firewalld=active
		ufw=inactive
		nftables=inactive
		@@mt:os-release
		NAME="Rocky Linux"
		VERSION="9.4 (Blue Onyx)"
		ID="rocky"
		ID_LIKE="rhel centos fedora"
		VERSION_ID="9.4"
		PRETTY_NAME="Rocky Linux 9.4 (Blue Onyx)"
		@@mt:sw_vers

		""";

	public const string Fedora40 = """
		@@mt:facts
		kernel_name=Linux
		kernel=Linux 6.10.6-200.fc40.x86_64 x86_64
		hostname=fedora
		uid=1000
		user=dev
		pid1=systemd
		init_link=../lib/systemd/systemd
		ssh=10.0.2.2 52000 10.0.2.15 22
		ufw=
		@@mt:commands
		systemctl
		journalctl
		timedatectl
		hostnamectl
		resolvectl
		dnf
		rpm
		firewall-cmd
		nft
		ip
		ss
		lsblk
		df
		ps
		kill
		sudo
		getent
		useradd
		usermod
		userdel
		chpasswd
		passwd
		chronyc
		@@mt:paths
		/run/systemd/system
		/etc/localtime
		/usr/share/zoneinfo
		/var/log/journal
		/etc/login.defs
		@@mt:units
		firewalld=active
		ufw=inactive
		nftables=inactive
		@@mt:os-release
		NAME="Fedora Linux"
		VERSION="40 (Server Edition)"
		ID=fedora
		VERSION_ID=40
		PRETTY_NAME="Fedora Linux 40 (Server Edition)"
		@@mt:sw_vers

		""";

	public const string CentOs7 = """
		@@mt:facts
		kernel_name=Linux
		kernel=Linux 3.10.0-1160.el7.x86_64 x86_64
		hostname=legacy
		uid=1000
		user=admin
		pid1=systemd
		init_link=../lib/systemd/systemd
		ssh=10.9.9.1 3333 10.9.9.2 22
		ufw=
		@@mt:commands
		systemctl
		journalctl
		timedatectl
		hostnamectl
		service
		chkconfig
		yum
		rpm
		firewall-cmd
		iptables
		ip
		ss
		netstat
		lsblk
		df
		ps
		kill
		crontab
		sudo
		getent
		useradd
		usermod
		userdel
		chpasswd
		passwd
		@@mt:paths
		/run/systemd/system
		/etc/init.d
		/etc/localtime
		/usr/share/zoneinfo
		/etc/crontab
		/etc/cron.d
		/run/log/journal
		/etc/login.defs
		/etc/rc.d/init.d
		@@mt:units
		firewalld=inactive
		ufw=unknown
		nftables=unknown
		@@mt:os-release
		NAME="CentOS Linux"
		VERSION="7 (Core)"
		ID="centos"
		ID_LIKE="rhel fedora"
		VERSION_ID="7"
		PRETTY_NAME="CentOS Linux 7 (Core)"
		@@mt:sw_vers

		""";

	public const string OpenSuseLeap = """
		@@mt:facts
		kernel_name=Linux
		kernel=Linux 6.4.0-150600.23.17-default x86_64
		hostname=leap
		uid=1000
		user=geeko
		pid1=systemd
		init_link=/usr/lib/systemd/systemd
		ssh=10.0.0.1 1111 10.0.0.2 22
		ufw=
		@@mt:commands
		systemctl
		journalctl
		timedatectl
		hostnamectl
		zypper
		rpm
		firewall-cmd
		nft
		iptables
		ip
		ss
		lsblk
		df
		ps
		kill
		crontab
		sudo
		getent
		useradd
		usermod
		userdel
		chpasswd
		passwd
		chronyc
		@@mt:paths
		/run/systemd/system
		/etc/localtime
		/usr/share/zoneinfo
		/etc/crontab
		/etc/cron.d
		/var/log/journal
		/etc/login.defs
		@@mt:units
		firewalld=inactive
		ufw=inactive
		nftables=inactive
		@@mt:os-release
		NAME="openSUSE Leap"
		VERSION="15.6"
		ID="opensuse-leap"
		ID_LIKE="suse opensuse"
		VERSION_ID="15.6"
		PRETTY_NAME="openSUSE Leap 15.6"
		@@mt:sw_vers

		""";

	public const string Arch = """
		@@mt:facts
		kernel_name=Linux
		kernel=Linux 6.10.10-arch1-1 x86_64
		hostname=archbox
		uid=1000
		user=me
		pid1=systemd
		init_link=../lib/systemd/systemd
		ssh=10.0.0.1 1111 10.0.0.3 22
		ufw=
		@@mt:commands
		systemctl
		journalctl
		timedatectl
		hostnamectl
		resolvectl
		pacman
		checkupdates
		nft
		iptables
		ip
		ss
		lsblk
		df
		ps
		kill
		sudo
		getent
		useradd
		usermod
		userdel
		chpasswd
		passwd
		@@mt:paths
		/run/systemd/system
		/etc/localtime
		/usr/share/zoneinfo
		/var/log/journal
		/etc/login.defs
		@@mt:units
		firewalld=inactive
		ufw=inactive
		nftables=active
		@@mt:os-release
		NAME="Arch Linux"
		PRETTY_NAME="Arch Linux"
		ID=arch
		BUILD_ID=rolling
		@@mt:sw_vers

		""";

	public const string Alpine = """
		@@mt:facts
		kernel_name=Linux
		kernel=Linux 6.6.51-0-lts x86_64
		hostname=alpine
		uid=1000
		user=al
		pid1=init
		init_link=/bin/busybox
		ssh=10.0.0.1 1111 10.0.0.4 22
		ufw=
		@@mt:commands
		openrc
		rc-service
		rc-update
		rc-status
		apk
		iptables
		ip
		netstat
		df
		ps
		kill
		crontab
		sudo
		getent
		adduser
		addgroup
		deluser
		chpasswd
		passwd
		busybox
		@@mt:paths
		/run/openrc
		/etc/init.d
		/etc/localtime
		/etc/crontab
		@@mt:units
		@@mt:os-release
		NAME="Alpine Linux"
		ID=alpine
		VERSION_ID=3.20.3
		PRETTY_NAME="Alpine Linux v3.20"
		@@mt:sw_vers

		""";

	public const string AlpineContainer = """
		@@mt:facts
		kernel_name=Linux
		kernel=Linux 6.10.4-linuxkit aarch64
		hostname=3f2c9a1b7d10
		uid=0
		user=root
		pid1=sshd
		init_link=/bin/busybox
		ssh=172.17.0.1 49000 172.17.0.2 22
		ufw=
		@@mt:commands
		apk
		ip
		netstat
		df
		ps
		kill
		crontab
		adduser
		addgroup
		deluser
		chpasswd
		passwd
		busybox
		@@mt:paths
		/etc/init.d
		/.dockerenv
		@@mt:units
		@@mt:os-release
		NAME="Alpine Linux"
		ID=alpine
		VERSION_ID=3.20.3
		PRETTY_NAME="Alpine Linux v3.20"
		@@mt:sw_vers

		""";

	/// <summary>Devuan: Debian without systemd, booted by sysvinit.</summary>
	public const string DevuanSysV = """
		@@mt:facts
		kernel_name=Linux
		kernel=Linux 6.1.0-18-amd64 x86_64
		hostname=devuan
		uid=1000
		user=dv
		pid1=init
		init_link=
		ssh=10.0.0.1 1111 10.0.0.5 22
		ufw=
		@@mt:commands
		service
		update-rc.d
		apt-get
		dpkg-query
		iptables
		ip
		ss
		lsblk
		df
		ps
		kill
		crontab
		sudo
		getent
		useradd
		usermod
		userdel
		chpasswd
		passwd
		@@mt:paths
		/etc/init.d
		/etc/timezone
		/etc/localtime
		/usr/share/zoneinfo
		/etc/crontab
		/etc/cron.d
		/etc/login.defs
		@@mt:units
		@@mt:os-release
		PRETTY_NAME="Devuan GNU/Linux 5 (daedalus)"
		NAME="Devuan GNU/Linux"
		VERSION_ID="5"
		ID=devuan
		ID_LIKE=debian
		@@mt:sw_vers

		""";

	public const string MacOs = """
		@@mt:facts
		kernel_name=Darwin
		kernel=Darwin 23.4.0 arm64
		hostname=studio.local
		uid=501
		user=pat
		pid1=
		init_link=
		ssh=10.0.0.1 1111 10.0.0.9 22
		ufw=
		@@mt:commands
		df
		ps
		kill
		crontab
		sudo
		sw_vers
		@@mt:paths
		/etc/localtime
		/usr/share/zoneinfo
		@@mt:units
		@@mt:os-release
		@@mt:sw_vers
		ProductName:		macOS
		ProductVersion:		14.4.1
		BuildVersion:		23E224

		""";

	public const string FreeBsd = """
		@@mt:facts
		kernel_name=FreeBSD
		kernel=FreeBSD 14.1-RELEASE amd64
		hostname=bsd
		uid=1001
		user=beastie
		pid1=
		init_link=
		ssh=10.0.0.1 1111 10.0.0.8 22
		ufw=
		@@mt:commands
		service
		df
		ps
		kill
		crontab
		sudo
		@@mt:paths
		/etc/localtime
		/usr/share/zoneinfo
		/etc/crontab
		@@mt:units
		@@mt:os-release
		NAME=FreeBSD
		VERSION="14.1-RELEASE"
		ID=freebsd
		PRETTY_NAME="FreeBSD 14.1-RELEASE"
		@@mt:sw_vers

		""";
}
