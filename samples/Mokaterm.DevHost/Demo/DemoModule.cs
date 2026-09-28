using Microsoft.Extensions.DependencyInjection.Extensions;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ssh.Components;
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
			Title = "Port forwarding",
			Icon = MokatermIcons.Transfers,
			Component = typeof(SshTunnelsView),
		});
	}
}
