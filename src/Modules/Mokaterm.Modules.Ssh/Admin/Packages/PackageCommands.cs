using Mokaterm.Abstractions.Commands;
using Mokaterm.Modules.Ssh.Admin.Detection;

namespace Mokaterm.Modules.Ssh.Admin.Packages;

/// <summary>The installed list, pending updates and package changes, in the words of the machine's package manager.</summary>
internal sealed class PackageCommands
{
	/// <summary>How long a check for updates gets: dnf and zypper may fetch repository metadata first.</summary>
	public static readonly TimeSpan UpdateCheckTimeout = TimeSpan.FromMinutes(3);

	// apt must never stop to ask: stdin is closed, so a debconf question or a changed config file would fail the run.
	private const string AptPrefix = "export DEBIAN_FRONTEND=noninteractive";

	private const string AptGet = "apt-get -y -o Dpkg::Options::=--force-confdef -o Dpkg::Options::=--force-confold";

	// What a machine needs to boot, or this session needs to stay up. Removing one is allowed, but never by accident.
	private static readonly string[] CriticalPrefixes =
	[
		"openssh", "ssh", "sudo", "systemd", "linux-image", "linux-generic", "kernel", "grub", "glibc", "libc6", "musl",
		"bash", "coreutils", "busybox", "apt", "dpkg", "dnf", "yum", "rpm", "zypper", "pacman", "apk-tools", "iproute2",
		"network-manager", "networkmanager", "netplan", "init", "dropbear",
	];

	public PackageCommands(PackageManagerKind kind)
	{
		if (kind == PackageManagerKind.None)
		{
			throw new ArgumentException("A machine without a package manager has no package commands.", nameof(kind));
		}

		Kind = kind;
	}

	public PackageManagerKind Kind { get; }

	/// <summary>The manager's name as the page shows it.</summary>
	public string DisplayName => Kind switch
	{
		PackageManagerKind.Apt => "APT",
		PackageManagerKind.Dnf => "DNF",
		PackageManagerKind.Yum => "YUM",
		PackageManagerKind.Zypper => "zypper",
		PackageManagerKind.Pacman => "pacman",
		_ => "apk",
	};

	/// <summary>Whether updates are listed from a cache that only root refreshes, so the page offers "Refresh" next to them.</summary>
	public bool HasRefresh => Kind != PackageManagerKind.Pacman;

	public AdminScript ListInstalled() => new()
	{
		Name = "packages.installed",
		Body = Kind switch
		{
			PackageManagerKind.Apt => """dpkg-query -W -f='${Package}\t${Version}\t${Architecture}\t${Status}\n'""",
			PackageManagerKind.Dnf or PackageManagerKind.Yum or PackageManagerKind.Zypper => """rpm -qa --queryformat '%{NAME}\t%{VERSION}-%{RELEASE}\t%{ARCH}\n'""",
			PackageManagerKind.Pacman => "pacman -Q",
			_ => "apk info -v 2>/dev/null",
		},
	};

	public IReadOnlyList<InstalledPackage> ParseInstalled(string output) => Kind switch
	{
		PackageManagerKind.Apt => PackageListParser.ParseDpkg(output),
		PackageManagerKind.Dnf or PackageManagerKind.Yum or PackageManagerKind.Zypper => PackageListParser.ParseRpm(output),
		PackageManagerKind.Pacman => PackageListParser.ParsePacman(output),
		_ => PackageListParser.ParseApkInstalled(output),
	};

	/// <summary>Pending updates from what the manager already knows. Only root can refresh that knowledge; see <see cref="RefreshIndex"/>.</summary>
	public AdminScript ListUpdates() => new()
	{
		Name = "packages.updates",
		Timeout = UpdateCheckTimeout,
		Body = UpdatesBody(cachedOnly: false),
	};

	/// <summary>
	/// Pending updates from the manager's cache alone, so nothing reaches the network: what a survey of many machines
	/// can afford. dnf and yum read the system cache, zypper skips its refresh and pacman reads its own sync database
	/// instead of syncing a scratch one; apt and apk only ever read their local lists here. It says nothing new about a
	/// machine whose cache is old, which is the price of reading forty of them in seconds.
	/// </summary>
	public AdminScript ListCachedUpdates() => new()
	{
		Name = "packages.updates.cached",
		Body = UpdatesBody(cachedOnly: true),
	};

	public IReadOnlyList<PackageUpdate> ParseUpdates(string output) => Kind switch
	{
		PackageManagerKind.Apt => PackageListParser.ParseAptUpgradable(output),
		PackageManagerKind.Dnf or PackageManagerKind.Yum => PackageListParser.ParseCheckUpdate(output),
		PackageManagerKind.Zypper => PackageListParser.ParseZypperUpdates(output),
		PackageManagerKind.Pacman => PackageListParser.ParsePacmanUpdates(output),
		_ => PackageListParser.ParseApkVersion(output),
	};

	/// <summary>
	/// Whether the manager says which pending updates fix security problems: apt by the suite an update comes from,
	/// dnf and yum by their advisories. pacman and apk keep no such list, and zypper keeps it as patches, not packages.
	/// </summary>
	public bool KnowsSecurityUpdates => Kind is PackageManagerKind.Apt or PackageManagerKind.Dnf or PackageManagerKind.Yum;

	/// <summary>
	/// The pending updates that fix security problems, from the same knowledge <see cref="ListUpdates"/> reads. Apt's
	/// is the same list, read by suite; null where <see cref="KnowsSecurityUpdates"/> is false.
	/// </summary>
	public AdminScript? ListSecurityUpdates() => Kind switch
	{
		PackageManagerKind.Apt => ListUpdates() with { Name = "packages.security" },
		PackageManagerKind.Dnf => new AdminScript { Name = "packages.security", Timeout = UpdateCheckTimeout, Body = CheckUpdate("dnf", cachedOnly: false, securityOnly: true) },
		PackageManagerKind.Yum => new AdminScript { Name = "packages.security", Timeout = UpdateCheckTimeout, Body = CheckUpdate("yum", cachedOnly: false, securityOnly: true) },
		_ => null,
	};

	public IReadOnlyList<PackageUpdate> ParseSecurityUpdates(string output) => Kind switch
	{
		PackageManagerKind.Apt => SecurityUpdatesAmong(PackageListParser.ParseAptUpgradable(output))!,
		PackageManagerKind.Dnf or PackageManagerKind.Yum => PackageListParser.ParseCheckUpdate(output),
		_ => [],
	};

	/// <summary>
	/// For apt, the security updates within a list <see cref="ListUpdates"/> already read, so a page that has that list
	/// needs no second read. Null for the managers whose advisories need <see cref="ListSecurityUpdates"/>.
	/// </summary>
	public IReadOnlyList<PackageUpdate>? SecurityUpdatesAmong(IReadOnlyList<PackageUpdate> updates)
	{
		ArgumentNullException.ThrowIfNull(updates);
		return Kind == PackageManagerKind.Apt ? [.. updates.Where(update => IsSecuritySource(update.Source))] : null;
	}

	/// <summary>
	/// An apt suite that carries security fixes: <c>noble-security</c>, <c>bookworm-security</c>, or Debian's older
	/// <c>buster/updates</c>. apt lists every suite an update comes from, comma separated.
	/// </summary>
	internal static bool IsSecuritySource(string source) =>
		source.Split(',').Any(suite => suite.EndsWith("-security", StringComparison.Ordinal) || suite.EndsWith("/updates", StringComparison.Ordinal));

	/// <summary>Fetches the repositories' current lists, so the next check sees what is new. Null for pacman, see <see cref="HasRefresh"/>.</summary>
	public AdminAction? RefreshIndex()
	{
		string? body = Kind switch
		{
			PackageManagerKind.Apt => "apt-get update",
			PackageManagerKind.Dnf => "dnf -q makecache",
			PackageManagerKind.Yum => "yum -q makecache",
			PackageManagerKind.Zypper => "zypper --non-interactive refresh",

			// pacman -Sy without -u is the partial upgrade Arch warns against.
			PackageManagerKind.Pacman => null,
			_ => "apk update",
		};

		return body is null ? null : new AdminAction
		{
			Title = "Refresh package lists",
			Script = Root("packages.refresh", body, TimeSpan.FromMinutes(10)),
			Description = "Fetches the current package lists from every repository. Nothing is installed.",
			ConfirmText = "Refresh",
		};
	}

	public AdminAction UpgradeAll() => new()
	{
		Title = "Upgrade all packages",
		Script = Root("packages.upgrade", Kind switch
		{
			PackageManagerKind.Apt => $"set -e\n{AptPrefix}\napt-get update\n{AptGet} upgrade",
			PackageManagerKind.Dnf => "dnf -y upgrade",
			PackageManagerKind.Yum => "yum -y update",
			PackageManagerKind.Zypper => "zypper --non-interactive update",
			PackageManagerKind.Pacman => "pacman -Syu --noconfirm",
			_ => "set -e\napk update\napk upgrade",
		}, AdminScript.PackageTimeout),
		Description = "Installs every available update. Services may restart, and a new kernel takes effect at the next boot.",
		Warning = "Closing this window leaves the upgrade running. Closing the session can stop it halfway, which leaves the package database to repair by hand.",
		ConfirmText = "Upgrade",
	};

	public AdminAction Install(string name)
	{
		Require(name);
		string quoted = PosixShell.Quote(name);
		return new AdminAction
		{
			Title = $"Install {name}",
			Script = Root("packages.install", Kind switch
			{
				PackageManagerKind.Apt => $"{AptPrefix}\n{AptGet} install {quoted}",
				PackageManagerKind.Dnf => $"dnf -y install {quoted}",
				PackageManagerKind.Yum => $"yum -y install {quoted}",
				PackageManagerKind.Zypper => $"zypper --non-interactive install {quoted}",
				PackageManagerKind.Pacman => $"pacman -S --noconfirm --needed {quoted}",
				_ => $"apk add {quoted}",
			}, AdminScript.PackageTimeout),
			Description = "Installs the package with whatever it depends on.",
			ConfirmText = "Install",
		};
	}

	public AdminAction Remove(string name)
	{
		Require(name);
		string quoted = PosixShell.Quote(name);
		return new AdminAction
		{
			Title = $"Remove {name}",
			Script = Root("packages.remove", Kind switch
			{
				PackageManagerKind.Apt => $"{AptPrefix}\n{AptGet} remove {quoted}",
				PackageManagerKind.Dnf => $"dnf -y remove {quoted}",
				PackageManagerKind.Yum => $"yum -y remove {quoted}",
				PackageManagerKind.Zypper => $"zypper --non-interactive remove {quoted}",
				PackageManagerKind.Pacman => $"pacman -R --noconfirm {quoted}",
				_ => $"apk del {quoted}",
			}, AdminScript.PackageTimeout),
			Description = "Removes the package, and on most managers whatever depends on it.",
			Warning = IsCritical(name)
				? "This package may be what the machine needs to boot, or what this session needs to stay connected."
				: null,
			CutsConnection = IsCritical(name),
			Destructive = true,
			ConfirmText = "Remove",
		};
	}

	/// <summary>The commands a manager needs on the machine before it counts as there, which detection and the survey both test.</summary>
	internal static IReadOnlyList<string> RequiredCommands(PackageManagerKind kind) => kind switch
	{
		PackageManagerKind.Apt => ["apt-get", "dpkg-query"],
		PackageManagerKind.Dnf => ["dnf", "rpm"],
		PackageManagerKind.Yum => ["yum", "rpm"],
		PackageManagerKind.Zypper => ["zypper", "rpm"],
		PackageManagerKind.Pacman => ["pacman"],
		PackageManagerKind.Apk => ["apk"],
		_ => [],
	};

	internal static bool IsCritical(string name)
	{
		string lower = name.ToLowerInvariant();
		return CriticalPrefixes.Any(prefix => lower == prefix || lower.StartsWith(prefix + "-", StringComparison.Ordinal) || lower.StartsWith(prefix + ":", StringComparison.Ordinal))
			|| lower.StartsWith("linux-image", StringComparison.Ordinal)
			|| lower.StartsWith("kernel", StringComparison.Ordinal);
	}

	// check-update exits 100 when there are updates, which is the answer and not a failure. -C reads the system cache,
	// which an ordinary login may read but never refresh.
	private static string CheckUpdate(string program, bool cachedOnly, bool securityOnly = false) =>
		$"{program} -q{(cachedOnly ? " -C" : "")}{(securityOnly ? " --security" : "")} check-update\ncode=$?\nif [ \"$code\" = 100 ]; then exit 0; fi\nexit \"$code\"";

	private string UpdatesBody(bool cachedOnly) => Kind switch
	{
		PackageManagerKind.Apt => "apt list --upgradable 2>/dev/null",
		PackageManagerKind.Dnf => CheckUpdate("dnf", cachedOnly),
		PackageManagerKind.Yum => CheckUpdate("yum", cachedOnly),
		PackageManagerKind.Zypper => cachedOnly
			? "zypper --non-interactive --no-refresh --quiet list-updates"
			: "zypper --non-interactive --quiet list-updates",

		// checkupdates syncs into a scratch database, so it sees new versions without root; it exits 2 for "none".
		// pacman -Qu reads the sync database as it stands and exits 1 for "none".
		PackageManagerKind.Pacman when cachedOnly => "pacman -Qu\nexit 0",
		PackageManagerKind.Pacman => string.Join('\n',
			"if command -v checkupdates >/dev/null 2>&1; then",
			"\tcheckupdates",
			"\tcode=$?",
			"\tif [ \"$code\" = 2 ]; then exit 0; fi",
			"\texit \"$code\"",
			"fi",
			"pacman -Qu",
			"exit 0"),
		_ => "apk version -l '<' 2>/dev/null",
	};

	private static AdminScript Root(string name, string body, TimeSpan timeout) => new()
	{
		Name = name,
		Body = body,
		AsRoot = true,
		Timeout = timeout,
		OutputLimitBytes = AdminScript.ActionOutputBytes,
	};

	private static void Require(string name)
	{
		if (AdminNames.CheckPackage(name) is { } problem)
		{
			throw new ArgumentException(problem, nameof(name));
		}
	}
}
