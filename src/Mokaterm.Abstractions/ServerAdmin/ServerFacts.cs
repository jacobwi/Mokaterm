using System.Text;
using System.Text.RegularExpressions;

namespace Mokaterm.Abstractions.ServerAdmin;

/// <summary>
/// What the System view found out about the server when it opened: the distribution, the kernel, the login, the init
/// system, the package manager and which commands exist. A page reads these instead of probing the server itself, and
/// a page's availability rule decides from them alone, because the rule runs every time the navigation is drawn.
/// </summary>
public sealed partial record ServerFacts
{
	/// <summary>The distribution's own name, such as "Ubuntu 24.04.1 LTS", or what the kernel says when it has none.</summary>
	public required string DisplayName { get; init; }

	/// <summary><c>ID</c> from os-release in lower case, such as <c>ubuntu</c> or <c>rocky</c>; empty when there is none.</summary>
	public string DistributionId { get; init; } = "";

	/// <summary><c>ID_LIKE</c> from os-release in lower case: the distributions this one follows.</summary>
	public IReadOnlyList<string> DistributionLike { get; init; } = [];

	/// <summary><c>VERSION_ID</c> from os-release, such as <c>24.04</c>.</summary>
	public string DistributionVersion { get; init; } = "";

	/// <summary><c>uname -srm</c>, such as <c>Linux 6.8.0-45-generic x86_64</c>.</summary>
	public string Kernel { get; init; } = "";

	public string HostName { get; init; } = "";

	/// <summary>The account this session logged in as.</summary>
	public string UserName { get; init; } = "";

	/// <summary>The session logged in as root itself, so nothing goes through sudo.</summary>
	public bool IsRootLogin { get; init; }

	/// <summary><c>systemd</c>, <c>openrc</c>, <c>sysvinit</c>, <c>busybox</c> or <c>none</c>.</summary>
	public string InitSystem { get; init; } = "none";

	/// <summary><c>apt</c>, <c>dnf</c>, <c>yum</c>, <c>zypper</c>, <c>pacman</c>, <c>apk</c> or <c>none</c>.</summary>
	public string PackageManager { get; init; } = "none";

	/// <summary><c>ufw</c>, <c>firewalld</c>, <c>nftables</c>, <c>iptables</c> or <c>none</c>.</summary>
	public string Firewall { get; init; } = "none";

	/// <summary>A container image rather than a booted machine: no service manager, and the clock is the host's.</summary>
	public bool IsContainer { get; init; }

	/// <summary>
	/// The commands the probe found, on the login's PATH or in the sbin folders: the System view's own list plus every
	/// command a registered page names in its descriptor. A command nobody named is not here even when it is installed.
	/// </summary>
	public IReadOnlySet<string> Commands { get; init; } = new HashSet<string>(StringComparer.Ordinal);

	public bool HasCommand(string command) => Commands.Contains(command);

	/// <summary>
	/// Whether <paramref name="name"/> can be probed: a plain program name such as <c>fail2ban-client</c>. Names go on
	/// the probe's command line as they are, so nothing a shell reads as syntax gets through.
	/// </summary>
	public static bool IsCommandName(string? name) => name is not null && CommandNamePattern().IsMatch(name);

	// The host and the account say which machine this is; nothing that prints a record should carry them.
	private bool PrintMembers(StringBuilder builder)
	{
		builder
			.Append("DisplayName = ").Append(DisplayName)
			.Append(", InitSystem = ").Append(InitSystem)
			.Append(", PackageManager = ").Append(PackageManager)
			.Append(", Commands = ").Append(Commands.Count);
		return true;
	}

	// \z rather than $, which would also match before a final line break.
	[GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9_.+-]{0,63}\z", RegexOptions.CultureInvariant)]
	private static partial Regex CommandNamePattern();
}
