using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Telnet.Components;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Extensions;

namespace Mokaterm.Modules.Telnet;

/// <summary>Telnet: the <c>telnet</c> protocol with its terminal, connection options editor and settings page.</summary>
public sealed class TelnetModule : IMokatermModule
{
	public const string ModuleId = "telnet";

	public ModuleInfo Info { get; } = new(
		ModuleId,
		"Telnet",
		"A terminal over plain TCP, with the option negotiation of RFC 854.",
		DisplayVersion.Of(typeof(TelnetModule).Assembly));

	public void ConfigureServices(IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddMokatermUiCommon();
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtocolProvider, TelnetProtocolProvider>());

		// Sessions expose a terminal channel, so the built-in terminal view shows them: no session view of our own.
		services.AddProtocolUi(new ProtocolUiDescriptor
		{
			ProtocolId = TelnetProtocolProvider.ProtocolId,
			Icon = TelnetIcons.Telnet,
			OptionsEditor = typeof(TelnetOptionsEditor),
		});

		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = "telnet",
			Title = "Telnet",
			Description = "Timeouts and idle behaviour for telnet sessions.",
			Group = SettingsPageGroup.Protocols,
			Icon = TelnetIcons.Telnet,
			Component = typeof(TelnetSettingsPage),
			Keywords = "nvt negotiation naws terminal type keepalive console serial",
		});
	}
}
