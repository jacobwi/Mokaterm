using Mokaterm.Abstractions.Modules;

namespace Mokaterm.Modules.Local;

public static class LocalMokatermBuilderExtensions
{
	/// <summary>
	/// Adds the local shell module: the <c>local</c> protocol with its options editor and settings page. Only a host
	/// that runs on the user's own computer should add it, which rules out the web host: there the shell would be the
	/// server's, handed to whoever opens the page.
	/// </summary>
	public static IMokatermBuilder AddLocal(this IMokatermBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		return builder.AddModule(new LocalModule());
	}
}
