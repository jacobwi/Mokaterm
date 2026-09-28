using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Rdp.Components;
using Mokaterm.Modules.Rdp.Interop;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Extensions;

namespace Mokaterm.Modules.Rdp;

/// <summary>RDP remote desktop: the <c>rdp</c> protocol, its session view, connection options editor and settings page.</summary>
public sealed class RdpModule : IMokatermModule
{
	public const string ModuleId = "rdp";

	public ModuleInfo Info { get; } = new(
		ModuleId,
		"RDP",
		"Remote desktop over RDP, decoded with IronRDP.",
		DisplayVersion.Of(typeof(RdpModule).Assembly));

	public void ConfigureServices(IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddMokatermUiCommon();
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtocolProvider, RdpProtocolProvider>());
		services.TryAddScoped<RdpInterop>();

		services.AddProtocolUi(new ProtocolUiDescriptor
		{
			ProtocolId = RdpProtocolProvider.ProtocolId,
			Icon = RdpIcons.RemoteDesktop,
			OptionsEditor = typeof(RdpOptionsEditor),
			SessionView = typeof(RdpSessionView),
		});

		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = "rdp",
			Title = "RDP",
			Description = "Timeouts, login attempts and how much of the screen an RDP session sends.",
			Group = SettingsPageGroup.Protocols,
			Icon = RdpIcons.RemoteDesktop,
			Component = typeof(RdpSettingsPage),
			Keywords = "remote desktop windows terminal services nla credssp frames",
		});
	}
}
