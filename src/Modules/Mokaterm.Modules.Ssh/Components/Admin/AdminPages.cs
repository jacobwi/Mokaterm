using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Core.Icons;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Common.ToolPages;

namespace Mokaterm.Modules.Ssh.Components.Admin;

/// <summary>
/// The System view's own pages, registered the way a plugin registers one, 100 apart so another module's page can sit
/// between any two. Their rules read the module's own capabilities through the view's context.
/// </summary>
internal static class AdminPages
{
	public static void Register(IServiceCollection services)
	{
		Add(services, AdminPageIds.Overview, "Overview", SshIcons.Pulse, typeof(AdminOverviewPage), 0);
		Add(services, AdminPageIds.Services, "Services", SshIcons.Gear, typeof(AdminServicesPage), 100, context =>
			context.Profile.Capabilities is { Services: null } capabilities ? capabilities.ServicesMissing : null);
		Add(services, AdminPageIds.Packages, "Packages", SshIcons.Package, typeof(AdminPackagesPage), 200, context =>
			context.Profile.Capabilities is { Packages: null } capabilities ? capabilities.PackagesMissing : null);
		Add(services, AdminPageIds.Cron, "Cron", SshIcons.Calendar, typeof(AdminCronPage), 300, context =>
			context.Profile.Capabilities is { HasCrontab: false, HasTimers: false } ? "Neither cron nor systemd timers are installed." : null);
		Add(services, AdminPageIds.Time, "Time", SshIcons.Clock, typeof(AdminTimePage), 400);
		Add(services, AdminPageIds.HostName, "Hostname", MokatermIcons.Tag, typeof(AdminHostNamePage), 500);
		Add(services, AdminPageIds.Network, "Network", SshIcons.Network, typeof(AdminNetworkPage), 600, context =>
			context.Profile.Capabilities.HasNetworkTools ? null : "There is no ip command (iproute2), so only name servers and ports can be read.");
		Add(services, AdminPageIds.Users, "Users", SshIcons.Users, typeof(AdminUsersPage), 700);
		Add(services, AdminPageIds.Disks, "Disks", SshIcons.Disk, typeof(AdminDisksPage), 800);
		Add(services, AdminPageIds.Processes, "Processes", SshIcons.Chip, typeof(AdminProcessesPage), 900, context =>
			context.Profile.Capabilities.HasProcessTools ? null : "There is no ps command.");
		Add(services, AdminPageIds.Logs, "Logs", SshIcons.Log, typeof(AdminLogsPage), 1000);
		Add(services, AdminPageIds.Firewall, "Firewall", MokatermIcons.Shield, typeof(AdminFirewallPage), 1100, context =>
			context.Profile.Capabilities.Firewall is null
				? context.Profile.Capabilities.FirewallMissing
				: context.Feature.RootBlockedReason is { } blocked ? $"The firewall only answers root. {blocked}" : null);
		Add(services, AdminPageIds.Security, "Security", MokatermIcons.ShieldCheck, typeof(AdminSecurityPage), 1200);
	}

	private static void Add(
		IServiceCollection services,
		string id,
		string title,
		MokaIconDefinition icon,
		Type component,
		int order,
		Func<AdminPageContext, string?>? unavailable = null) =>
		services.AddSystemPage(new SystemPageDescriptor
		{
			Id = id,
			Title = title,
			Icon = icon,
			Component = component,
			Order = order,

			// The view always hands its pages its own context; anything else has nothing these rules can read.
			Unavailable = unavailable is null ? null : (ISystemPageContext context) => context is AdminPageContext admin ? unavailable(admin) : null,
		});
}
