using Mokaterm.Abstractions.Modules;

namespace Mokaterm.Modules.Serial;

public static class SerialMokatermBuilderExtensions
{
	/// <summary>
	/// Adds the serial module: the <c>serial</c> protocol with its options editor, settings page and session panel.
	/// Only a host whose own machine may be reached through its serial ports should add it, which rules out the web
	/// host: there the ports would be the server's.
	/// </summary>
	public static IMokatermBuilder AddSerial(this IMokatermBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		return builder.AddModule(new SerialModule());
	}
}
