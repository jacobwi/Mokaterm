using Mokaterm.Abstractions.Modules;

namespace Mokaterm.Modules.Telnet;

public static class TelnetMokatermBuilderExtensions
{
	/// <summary>Adds the telnet module: the <c>telnet</c> protocol with its options editor and settings page.</summary>
	public static IMokatermBuilder AddTelnet(this IMokatermBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		return builder.AddModule(new TelnetModule());
	}
}
