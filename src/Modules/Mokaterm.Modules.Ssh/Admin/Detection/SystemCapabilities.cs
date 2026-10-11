using Mokaterm.Modules.Ssh.Admin.Accounts;
using Mokaterm.Modules.Ssh.Admin.Clock;
using Mokaterm.Modules.Ssh.Admin.Firewall;
using Mokaterm.Modules.Ssh.Admin.Naming;
using Mokaterm.Modules.Ssh.Admin.Packages;
using Mokaterm.Modules.Ssh.Admin.Services;

namespace Mokaterm.Modules.Ssh.Admin.Detection;

/// <summary>
/// The command sets a machine supports, built from its <see cref="SystemProfile"/>. A null set means the page has
/// nothing to drive (no init system, no package manager), and the reason is the matching <c>...Missing</c> text.
/// </summary>
internal sealed class SystemCapabilities
{
	public SystemCapabilities(SystemProfile profile)
	{
		ArgumentNullException.ThrowIfNull(profile);
		SystemFacts facts = profile.Facts;
		Services = profile.Init == InitSystem.None ? null : new ServiceCommands(profile.Init, facts);
		Packages = profile.PackageManager == PackageManagerKind.None ? null : new PackageCommands(profile.PackageManager);
		Firewall = profile.Firewall == FirewallKind.None ? null : new FirewallCommands(profile.Firewall, facts.SshServerPort ?? 22);
		Clock = new ClockCommands(profile.HasSystemdTools && facts.Has("timedatectl"));
		HostName = new HostNameCommands(profile.HasSystemdTools && facts.Has("hostnamectl"));
		Accounts = new AccountCommands(profile.AccountTools, facts.Has("chpasswd"));
		HasCrontab = facts.Has("crontab");
		HasTimers = profile.Init == InitSystem.Systemd;
		HasJournal = profile.HasJournal;
		HasNetworkTools = facts.Has("ip");
		HasProcessTools = facts.Has("ps");

		ServicesMissing = profile.IsContainer
			? "No init system runs here, which is usual in a container: services are whatever the container started."
			: "No service manager this view knows (systemd, OpenRC, sysvinit) runs on this machine.";
		PackagesMissing = "No package manager this view knows (apt, dnf, yum, zypper, pacman, apk) is installed.";
		FirewallMissing = "No firewall tool (ufw, firewalld, nft, iptables) is installed.";
	}

	public ServiceCommands? Services { get; }

	public PackageCommands? Packages { get; }

	public FirewallCommands? Firewall { get; }

	public ClockCommands Clock { get; }

	public HostNameCommands HostName { get; }

	public AccountCommands Accounts { get; }

	public bool HasCrontab { get; }

	/// <summary>systemd timers, the other scheduler, listed beside cron.</summary>
	public bool HasTimers { get; }

	public bool HasJournal { get; }

	public bool HasNetworkTools { get; }

	public bool HasProcessTools { get; }

	public string ServicesMissing { get; }

	public string PackagesMissing { get; }

	public string FirewallMissing { get; }
}
