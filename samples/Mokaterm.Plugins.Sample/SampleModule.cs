using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Plugins;
using Mokaterm.Plugins.Sample;
using Mokaterm.Plugins.Sample.Fail2ban;
using Mokaterm.Plugins.Sample.Subnets;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;

[assembly: MokatermPlugin(typeof(SampleModule))]

namespace Mokaterm.Plugins.Sample;

public sealed class SampleModule : IMokatermModule
{
	// The DevHost's demo protocol and the desktop's local shell, so the tool opens on either host without a server.
	private static readonly string[] Protocols = ["demo", "local"];

	public ModuleInfo Info { get; } = new("sample-plugin", "Sample plugin", "Shows that a plugin's views, pages, tools, script and styles load.", "1.0.0");

	public void ConfigureServices(IServiceCollection services)
	{
		foreach (string protocol in Protocols)
		{
			services.AddSessionTool(new SessionToolDescriptor
			{
				ProtocolId = protocol,
				Id = "sample-plugin",
				Title = "Sample plugin",
				Icon = MokatermIcons.Tag,
				Component = typeof(SamplePluginTool),
				ShowWhileDisconnected = true,
				Order = 900,
			});
		}

		// After the System view's Firewall page and before Security, which sit at 1100 and 1200.
		services.AddSystemPage(new SystemPageDescriptor
		{
			Id = "fail2ban",
			Title = "Fail2ban",
			Icon = MokatermIcons.Shield,
			Component = typeof(Fail2banPage),
			Order = 1150,
			Commands = [Fail2banCommands.Client],
			Unavailable = context => Fail2banCommands.Unavailable(context.Server),
		});

		// The page id starts with plugin. and the plugin's id, like its settings key; the loader refuses anything else.
		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = SampleSettings.SectionKey,
			Title = "Sample plugin",
			Description = "How often the Fail2ban page refreshes, and what the subnet calculator opens with.",
			Group = SettingsPageGroup.Plugins,
			Icon = MokatermIcons.Plugin,
			Component = typeof(SampleSettingsPage),
			Keywords = "sample plugin fail2ban refresh subnet calculator",
		});

		services.AddAppTool(new AppToolDescriptor
		{
			Id = "subnet-calculator",
			Title = "Subnet calculator",
			Icon = MokatermIcons.Globe,
			Description = "Network, broadcast, host range, host count and mask of an IPv4 or IPv6 prefix",
			Component = typeof(SubnetCalculatorTool),
		});
	}
}
