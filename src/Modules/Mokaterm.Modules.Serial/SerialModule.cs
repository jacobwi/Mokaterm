using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Serial.Components;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Extensions;

namespace Mokaterm.Modules.Serial;

/// <summary>
/// Serial: a console over a port on this machine, with its terminal, connection options editor, settings page and a
/// panel for the live line. Registered only by hosts that run on the machine the ports belong to.
/// </summary>
public sealed class SerialModule : IMokatermModule
{
	public const string ModuleId = "serial";

	public ModuleInfo Info { get; } = new(
		ModuleId,
		"Serial",
		"A console over a serial port or USB adapter, with the line settings and the control pins.",
		DisplayVersion.Of(typeof(SerialModule).Assembly));

	public void ConfigureServices(IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddMokatermUiCommon();
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtocolProvider, SerialProtocolProvider>());

		// Sessions expose a terminal channel, so the built-in terminal view shows them: no session view of our own.
		services.AddProtocolUi(new ProtocolUiDescriptor
		{
			ProtocolId = SerialProtocolProvider.ProtocolId,
			Icon = SerialIcons.Serial,
			OptionsEditor = typeof(SerialOptionsEditor),
		});

		services.AddSessionTool(new SessionToolDescriptor
		{
			ProtocolId = SerialProtocolProvider.ProtocolId,
			Title = "Serial line",
			Icon = SerialIcons.Signals,
			Component = typeof(SerialLineView),
		});

		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = "serial",
			Title = "Serial",
			Description = "The line a new serial connection starts from, and the timeouts every port is opened with.",
			Group = SettingsPageGroup.Protocols,
			Icon = SerialIcons.Serial,
			Component = typeof(SerialSettingsPage),
			Keywords = "serial com port baud parity stop bits flow control rts cts dtr xon xoff break console uart usb ftdi",
		});
	}
}
