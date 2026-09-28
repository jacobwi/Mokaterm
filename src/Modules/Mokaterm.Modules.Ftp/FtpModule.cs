using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ftp.Components;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;

namespace Mokaterm.Modules.Ftp;

/// <summary>FTP and FTPS file access: the <c>ftp</c> protocol, its connection options editor and its settings page.</summary>
public sealed class FtpModule : IMokatermModule
{
	public const string ModuleId = "ftp";

	public ModuleInfo Info { get; } = new(
		ModuleId,
		"FTP & FTPS",
		"Browse and transfer files over FTP, with explicit or implicit TLS.",
		DisplayVersion.Of(typeof(FtpModule).Assembly));

	public void ConfigureServices(IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.TryAddSingleton(TimeProvider.System);
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtocolProvider, FtpProtocolProvider>());

		services.AddProtocolUi(new ProtocolUiDescriptor
		{
			ProtocolId = FtpProtocolProvider.ProtocolId,
			Icon = MokatermIcons.Globe,
			OptionsEditor = typeof(FtpOptionsEditor),
		});

		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = "ftp",
			Title = "FTP",
			Description = "Timeouts, keepalive and login attempts for FTP and FTPS connections.",
			Group = SettingsPageGroup.Protocols,
			Icon = MokatermIcons.Globe,
			Component = typeof(FtpSettingsPage),
			Keywords = "ftps tls passive timeout",
		});
	}
}
