using Mokaterm.Abstractions.Modules;

namespace Mokaterm.Modules.Rdp;

public static class RdpMokatermBuilderExtensions
{
	/// <summary>Adds the RDP module: the <c>rdp</c> protocol with its session view, options editor and settings page.</summary>
	public static IMokatermBuilder AddRdp(this IMokatermBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		return builder.AddModule(new RdpModule());
	}
}
