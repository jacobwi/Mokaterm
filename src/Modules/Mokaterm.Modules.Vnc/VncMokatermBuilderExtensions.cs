using Mokaterm.Abstractions.Modules;

namespace Mokaterm.Modules.Vnc;

public static class VncMokatermBuilderExtensions
{
	/// <summary>Adds the VNC module: the <c>vnc</c> protocol with its session view, options editor and settings page.</summary>
	public static IMokatermBuilder AddVnc(this IMokatermBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		return builder.AddModule(new VncModule());
	}
}
