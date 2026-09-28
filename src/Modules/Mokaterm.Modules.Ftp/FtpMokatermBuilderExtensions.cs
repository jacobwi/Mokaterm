using Mokaterm.Abstractions.Modules;

namespace Mokaterm.Modules.Ftp;

public static class FtpMokatermBuilderExtensions
{
	/// <summary>Adds the FTP module: the <c>ftp</c> protocol with FTPS, its connection options editor and its settings page.</summary>
	public static IMokatermBuilder AddFtp(this IMokatermBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		return builder.AddModule(new FtpModule());
	}
}
