using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Vnc.Components;
using Mokaterm.Modules.Vnc.Interop;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Common.Icons;

namespace Mokaterm.Modules.Vnc;

/// <summary>VNC remote desktop: the <c>vnc</c> protocol, its session view, connection options editor and settings page.</summary>
public sealed class VncModule : IMokatermModule
{
	public const string ModuleId = "vnc";

	public ModuleInfo Info { get; } = new(
		ModuleId,
		"VNC",
		"Remote desktop over VNC (RFB), with TLS through VeNCrypt.",
		DisplayVersion.Of(typeof(VncModule).Assembly));

	public void ConfigureServices(IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddMokatermUiCommon();
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtocolProvider, VncProtocolProvider>());
		services.TryAddScoped<VncInterop>();

		services.AddProtocolUi(new ProtocolUiDescriptor
		{
			ProtocolId = VncProtocolProvider.ProtocolId,
			Icon = MokatermIcons.Monitor,
			OptionsEditor = typeof(VncOptionsEditor),
			SessionView = typeof(VncSessionView),
		});

		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = "vnc",
			Title = "VNC",
			Description = "Timeouts, login attempts and screen behaviour for VNC sessions.",
			Group = SettingsPageGroup.Protocols,
			Icon = MokatermIcons.Monitor,
			Component = typeof(VncSettingsPage),
			Keywords = "rfb remote desktop vencrypt clipboard cursor",
		});
	}
}
