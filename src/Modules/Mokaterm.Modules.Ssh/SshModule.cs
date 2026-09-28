using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ssh.Components;
using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.Keys;
using Mokaterm.Modules.Ssh.Protocols;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;

namespace Mokaterm.Modules.Ssh;

/// <summary>SSH terminal sessions and SFTP file browsing, with sudo elevation, private key validation and host key checks.</summary>
public sealed class SshModule : IMokatermModule
{
	public ModuleInfo Info { get; } = new(
		"ssh",
		"SSH & SFTP",
		"Terminal sessions and file browsing over SSH, with sudo for files owned by root.",
		DisplayVersion.Of(typeof(SshModule).Assembly));

	public void ConfigureServices(IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);
		services.TryAddSingleton<SshConnector>();
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtocolProvider, SshProtocolProvider>());
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtocolProvider, SftpProtocolProvider>());
		services.TryAddSingleton<IPrivateKeyInspector, SshPrivateKeyInspector>();

		services.AddProtocolUi(new ProtocolUiDescriptor
		{
			ProtocolId = SshProtocolIds.Ssh,
			Icon = MokatermIcons.Terminal,
			OptionsEditor = typeof(SshOptionsEditor),
		});

		services.AddProtocolUi(new ProtocolUiDescriptor
		{
			ProtocolId = SshProtocolIds.Sftp,
			Icon = MokatermIcons.FolderOpen,
			OptionsEditor = typeof(SshOptionsEditor),
		});

		services.AddSessionTool(new SessionToolDescriptor
		{
			ProtocolId = SshProtocolIds.Ssh,
			Title = "Port forwarding",
			Icon = SshIcons.Tunnel,
			Component = typeof(SshTunnelsView),
		});

		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = "ssh",
			Title = "SSH",
			Description = "Keepalives, timeouts, login attempts, the SSH agent and running file operations as root.",
			Icon = MokatermIcons.Terminal,
			Component = typeof(SshSettingsPage),
			Group = SettingsPageGroup.Protocols,
			Keywords = "keepalive timeout sudo sftp agent tunnel forward",
		});
	}
}
