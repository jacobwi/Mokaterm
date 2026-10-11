using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Local.Components;
using Mokaterm.Modules.Local.Containers;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Extensions;

namespace Mokaterm.Modules.Local;

/// <summary>
/// Local shell: PowerShell, the Command Prompt, Git Bash or a WSL distribution on this computer, and a shell in a Docker
/// container or a Kubernetes pod through this computer's docker.exe or kubectl.exe, with their connection options
/// editors and the settings page. Registered only by hosts that run on the user's own machine.
/// </summary>
public sealed class LocalModule : IMokatermModule
{
	public const string ModuleId = "local";

	public ModuleInfo Info { get; } = new(
		ModuleId,
		"Local shell",
		"A shell on this computer through its pseudo console: PowerShell, the Command Prompt, Git Bash, a WSL distribution, or a shell in a Docker container or Kubernetes pod through docker exec or kubectl exec.",
		DisplayVersion.Of(typeof(LocalModule).Assembly));

	public void ConfigureServices(IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddMokatermUiCommon();
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtocolProvider, LocalProtocolProvider>());
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtocolProvider, DockerShellProvider>());
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtocolProvider, KubernetesShellProvider>());

		// Sessions expose a terminal channel, so the built-in terminal view shows them: no session view of our own.
		services.AddProtocolUi(new ProtocolUiDescriptor
		{
			ProtocolId = LocalProtocolProvider.ProtocolId,
			Icon = LocalIcons.Shell,
			OptionsEditor = typeof(LocalOptionsEditor),
		});

		services.AddProtocolUi(new ProtocolUiDescriptor
		{
			ProtocolId = DockerShellProvider.ProtocolId,
			Icon = LocalIcons.Container,
			OptionsEditor = typeof(DockerShellOptionsEditor),
		});

		services.AddProtocolUi(new ProtocolUiDescriptor
		{
			ProtocolId = KubernetesShellProvider.ProtocolId,
			Icon = LocalIcons.Pod,
			OptionsEditor = typeof(KubernetesShellOptionsEditor),
		});

		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = "local",
			Title = "Local shell",
			Description = "The shell a login on this computer runs when it names none, where it starts, and the shells found here.",
			Group = SettingsPageGroup.Protocols,
			Icon = LocalIcons.Shell,
			Component = typeof(LocalSettingsPage),
			Keywords = "local shell terminal console conpty powershell pwsh cmd command prompt git bash wsl linux ubuntu debian starting folder exit code docker container kubernetes kubectl pod exec",
		});
	}
}
