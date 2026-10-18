using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Modules.Ssh.Admin.Reboot;
using Mokaterm.Modules.Ssh.Components;
using Mokaterm.Modules.Ssh.Components.Admin;
using Mokaterm.Modules.Ssh.Components.Docker;
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

		// Built on first use, from the session manager and prompts the host supplies; the module itself offers neither.
		services.TryAddScoped(provider => new RebootReconnects(
			provider.GetRequiredService<ISessionManager>(),
			provider.GetRequiredService<IUserInteraction>(),
			provider.GetService<TimeProvider>() ?? TimeProvider.System,
			provider.GetRequiredService<ILogger<RebootReconnects>>()));

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
			Id = "port-forwarding",
			Title = "Port forwarding",
			Icon = SshIcons.Tunnel,
			Component = typeof(SshTunnelsView),
		});

		services.AddSessionTool(new SessionToolDescriptor
		{
			ProtocolId = SshProtocolIds.Ssh,
			Id = SystemAdminView.ToolId,
			Title = "System",
			Icon = MokatermIcons.Server,
			Component = typeof(SystemAdminView),
			Order = 1,
			Size = SessionToolSize.Wide,
		});

		services.AddSessionTool(new SessionToolDescriptor
		{
			ProtocolId = SshProtocolIds.Ssh,
			Id = "docker",
			Title = "Docker",
			Icon = DockerIcons.Container,
			Component = typeof(DockerManagerView),
			Order = 10,
			Size = SessionToolSize.Wide,
		});

		AdminPages.Register(services);
		DockerPages.Register(services);

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
