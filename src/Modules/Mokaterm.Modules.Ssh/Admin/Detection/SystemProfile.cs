using Mokaterm.Abstractions.ServerAdmin;
using Mokaterm.Modules.Ssh.Admin.Packages;

namespace Mokaterm.Modules.Ssh.Admin.Detection;

/// <summary>
/// What the server is, decided once from <see cref="SystemFacts"/>: the family, the init system, the package manager,
/// the firewall and the tools around them. Pages ask <see cref="Capabilities"/> for the commands that fit instead of
/// looking at distribution names themselves.
/// </summary>
internal sealed record SystemProfile
{
	private SystemCapabilities? _capabilities;
	private ServerFacts? _serverFacts;

	/// <summary>
	/// The order package managers are looked for when the distribution does not name one. The survey's script picks a
	/// manager in the same order, since it has to choose before anything is detected.
	/// </summary>
	public static IReadOnlyList<PackageManagerKind> PackageManagerSearchOrder { get; } =
	[
		PackageManagerKind.Apt, PackageManagerKind.Dnf, PackageManagerKind.Yum, PackageManagerKind.Zypper, PackageManagerKind.Pacman, PackageManagerKind.Apk,
	];

	public required SystemFacts Facts { get; init; }

	public PlatformKind Platform { get; init; }

	public DistroFamily Family { get; init; }

	public InitSystem Init { get; init; }

	public PackageManagerKind PackageManager { get; init; }

	public FirewallKind Firewall { get; init; }

	public AccountToolKind AccountTools { get; init; }

	/// <summary>A container image rather than a booted machine: no service manager, and the clock is the host's.</summary>
	public bool IsContainer { get; init; }

	/// <summary>Why this view does not manage the machine at all, or null for Linux.</summary>
	public string? UnsupportedReason { get; init; }

	/// <summary>The command sets for this machine, built on first use.</summary>
	public SystemCapabilities Capabilities => _capabilities ??= new SystemCapabilities(this);

	/// <summary>What pages from other modules see of this profile, built on first use.</summary>
	public ServerFacts ServerFacts => _serverFacts ??= new ServerFacts
	{
		DisplayName = DisplayName,
		DistributionId = Facts.OsRelease.Id,
		DistributionLike = Facts.OsRelease.IdLike,
		DistributionVersion = Facts.OsRelease.VersionId,
		Kernel = Facts.Kernel,
		HostName = Facts.HostName,
		UserName = Facts.UserName,
		IsRootLogin = Facts.IsRoot,
		InitSystem = Init switch
		{
			InitSystem.Systemd => "systemd",
			InitSystem.OpenRc => "openrc",
			InitSystem.SysV => "sysvinit",
			InitSystem.BusyBox => "busybox",
			_ => "none",
		},
		PackageManager = PackageManager switch
		{
			PackageManagerKind.Apt => "apt",
			PackageManagerKind.Dnf => "dnf",
			PackageManagerKind.Yum => "yum",
			PackageManagerKind.Zypper => "zypper",
			PackageManagerKind.Pacman => "pacman",
			PackageManagerKind.Apk => "apk",
			_ => "none",
		},
		Firewall = Firewall switch
		{
			FirewallKind.Ufw => "ufw",
			FirewallKind.Firewalld => "firewalld",
			FirewallKind.Nftables => "nftables",
			FirewallKind.Iptables => "iptables",
			_ => "none",
		},
		IsContainer = IsContainer,
		Commands = Facts.Commands,
	};

	/// <summary>timedatectl, hostnamectl and journalctl talk to systemd's services, which only run when systemd booted the machine.</summary>
	public bool HasSystemdTools => Init == InitSystem.Systemd;

	public bool HasJournal => HasSystemdTools && Facts.Has("journalctl");

	/// <summary>The distribution's own name, or what the kernel says when it has none.</summary>
	public string DisplayName
	{
		get
		{
			if (Facts.OsRelease.PrettyName is { Length: > 0 } pretty)
			{
				return pretty;
			}

			if (Facts.MacProduct.Length > 0)
			{
				return Facts.MacProduct;
			}

			string name = Facts.OsRelease.Name;
			string version = Facts.OsRelease.VersionId;
			return name.Length > 0 ? $"{name} {version}".Trim() : Facts.KernelName.Length > 0 ? Facts.KernelName : "Unknown system";
		}
	}

	public static SystemProfile From(SystemFacts facts)
	{
		ArgumentNullException.ThrowIfNull(facts);
		PlatformKind platform = PlatformOf(facts.KernelName);
		DistroFamily family = platform == PlatformKind.Linux ? FamilyOf(facts.OsRelease) : DistroFamily.Unknown;
		InitSystem init = platform == PlatformKind.Linux ? InitOf(facts) : InitSystem.None;
		return new SystemProfile
		{
			Facts = facts,
			Platform = platform,
			Family = family,
			Init = init,
			PackageManager = platform == PlatformKind.Linux ? PackageManagerOf(family, facts) : PackageManagerKind.None,
			Firewall = platform == PlatformKind.Linux ? FirewallOf(facts) : FirewallKind.None,
			AccountTools = platform != PlatformKind.Linux ? AccountToolKind.None
				: facts.Has("useradd") ? AccountToolKind.Shadow
				: facts.Has("adduser") && facts.Has("deluser") ? AccountToolKind.BusyBox
				: AccountToolKind.None,
			IsContainer = platform == PlatformKind.Linux
				&& (facts.Exists("/.dockerenv") || facts.Exists("/run/.containerenv") || init == InitSystem.None),
			UnsupportedReason = platform switch
			{
				PlatformKind.Linux => null,
				PlatformKind.MacOs => "This view manages Linux servers. macOS has its own tools for services, packages and users.",
				PlatformKind.Bsd => $"This view manages Linux servers. {facts.KernelName} names its services, packages and firewall differently.",
				_ => facts.KernelName.Length > 0
					? $"This view manages Linux servers, and this one says it runs {facts.KernelName}."
					: "This view manages Linux servers, and this one did not say what it runs.",
			},
		};
	}

	internal static PlatformKind PlatformOf(string kernelName) => kernelName switch
	{
		"Linux" => PlatformKind.Linux,
		"Darwin" => PlatformKind.MacOs,
		_ when kernelName.EndsWith("BSD", StringComparison.Ordinal) || kernelName == "DragonFly" => PlatformKind.Bsd,
		_ => PlatformKind.Other,
	};

	internal static DistroFamily FamilyOf(OsRelease release)
	{
		foreach (string id in (IEnumerable<string>)[release.Id, .. release.IdLike])
		{
			DistroFamily family = id switch
			{
				"debian" or "ubuntu" or "raspbian" or "linuxmint" or "pop" or "kali" or "devuan" => DistroFamily.Debian,
				"rhel" or "fedora" or "centos" or "rocky" or "almalinux" or "ol" or "amzn" or "scientific" => DistroFamily.RedHat,
				"suse" or "opensuse" or "sles" or "sled" => DistroFamily.Suse,
				_ when id.StartsWith("opensuse", StringComparison.Ordinal) => DistroFamily.Suse,
				"arch" or "manjaro" or "endeavouros" => DistroFamily.Arch,
				"alpine" => DistroFamily.Alpine,
				_ => DistroFamily.Unknown,
			};

			if (family != DistroFamily.Unknown)
			{
				return family;
			}
		}

		return DistroFamily.Unknown;
	}

	internal static InitSystem InitOf(SystemFacts facts)
	{
		// systemd's own test for "booted with systemd" (sd_booted): the directory only exists while it runs as PID 1.
		if (facts.Exists("/run/systemd/system"))
		{
			return InitSystem.Systemd;
		}

		if (facts.Exists("/run/openrc") && facts.Has("rc-service"))
		{
			return InitSystem.OpenRc;
		}

		if (facts.Pid1 != "init")
		{
			return InitSystem.None;
		}

		if (facts.InitLink.Contains("busybox", StringComparison.Ordinal))
		{
			return InitSystem.BusyBox;
		}

		return facts.Exists("/etc/init.d") || facts.Exists("/etc/rc.d/init.d") ? InitSystem.SysV : InitSystem.None;
	}

	internal static PackageManagerKind PackageManagerOf(DistroFamily family, SystemFacts facts)
	{
		PackageManagerKind expected = family switch
		{
			DistroFamily.Debian => PackageManagerKind.Apt,
			DistroFamily.RedHat => facts.Has("dnf") ? PackageManagerKind.Dnf : PackageManagerKind.Yum,
			DistroFamily.Suse => PackageManagerKind.Zypper,
			DistroFamily.Arch => PackageManagerKind.Pacman,
			DistroFamily.Alpine => PackageManagerKind.Apk,
			_ => PackageManagerKind.None,
		};

		if (expected != PackageManagerKind.None && IsUsable(expected, facts))
		{
			return expected;
		}

		// An unknown or trimmed distribution: whichever manager is actually there.
		foreach (PackageManagerKind candidate in PackageManagerSearchOrder)
		{
			if (IsUsable(candidate, facts))
			{
				return candidate;
			}
		}

		return PackageManagerKind.None;
	}

	internal static FirewallKind FirewallOf(SystemFacts facts)
	{
		bool firewalld = facts.Has("firewall-cmd");
		bool ufw = facts.Has("ufw");

		// The one that is actually filtering wins; between two installed and idle ones, the distribution's own front end.
		if (firewalld && facts.IsActive("firewalld"))
		{
			return FirewallKind.Firewalld;
		}

		if (ufw && (facts.UfwEnabled == true || facts.IsActive("ufw")))
		{
			return FirewallKind.Ufw;
		}

		if (firewalld)
		{
			return FirewallKind.Firewalld;
		}

		if (ufw)
		{
			return FirewallKind.Ufw;
		}

		if (facts.Has("nft"))
		{
			return FirewallKind.Nftables;
		}

		return facts.Has("iptables") ? FirewallKind.Iptables : FirewallKind.None;
	}

	private static bool IsUsable(PackageManagerKind kind, SystemFacts facts) =>
		kind != PackageManagerKind.None && PackageCommands.RequiredCommands(kind).All(facts.Has);
}
