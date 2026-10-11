using Microsoft.Extensions.DependencyInjection.Extensions;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ssh.Components;
using Mokaterm.Modules.Ssh.Components.Admin;
using Mokaterm.Modules.Ssh.Components.Docker;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;

namespace Mokaterm.DevHost.Demo;

/// <summary>The <c>demo</c> protocol: a fake bash and an in-memory file system, so the shell can be worked on without servers.</summary>
internal sealed class DemoModule : IMokatermModule
{
	public ModuleInfo Info { get; } = new(
		"demo",
		"Demo shell",
		"In-memory shell and file system for UI work.",
		DisplayVersion.Of(typeof(DemoModule).Assembly));

	public void ConfigureServices(IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtocolProvider, DemoProtocolProvider>());
		services.AddProtocolUi(new ProtocolUiDescriptor
		{
			ProtocolId = DemoProtocolProvider.ProtocolId,
			Icon = MokatermIcons.Terminal,
		});

		// The real tunnels panel over a demo feature, so the session tool contribution point can be looked at here.
		services.AddSessionTool(new SessionToolDescriptor
		{
			ProtocolId = DemoProtocolProvider.ProtocolId,
			Id = "port-forwarding",
			Title = "Port forwarding",
			Icon = MokatermIcons.Transfers,
			Component = typeof(SshTunnelsView),
		});

		// The real system view over the demo's command feature, which answers its reads from canned output.
		services.AddSessionTool(new SessionToolDescriptor
		{
			ProtocolId = DemoProtocolProvider.ProtocolId,
			Id = "system",
			Title = "System",
			Icon = MokatermIcons.Server,
			Component = typeof(SystemAdminView),
			Order = 1,
			Size = SessionToolSize.Wide,
		});

		// The real Docker manager over an engine that lives in memory (Demo/Docker), for the same reason.
		services.AddSessionTool(new SessionToolDescriptor
		{
			ProtocolId = DemoProtocolProvider.ProtocolId,
			Id = "docker",
			Title = "Docker",
			Icon = MokatermIcons.Modules,
			Component = typeof(DockerManagerView),
			Order = 10,
			Size = SessionToolSize.Wide,
		});
	}
}
