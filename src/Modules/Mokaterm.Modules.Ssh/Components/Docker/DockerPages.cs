using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Core.Icons;
using Mokaterm.UI.Common.Contributions;

namespace Mokaterm.Modules.Ssh.Components.Docker;

/// <summary>
/// The Docker manager's own pages, registered the way a plugin registers one, 100 apart so another module's page can
/// sit between any two. The manager opens on <see cref="DockerPageIds.Containers"/>.
/// </summary>
internal static class DockerPages
{
	public static void Register(IServiceCollection services)
	{
		Add(services, DockerPageIds.Overview, "Overview", DockerIcons.Overview, typeof(DockerOverviewPage), 0);
		Add(services, DockerPageIds.Containers, "Containers", DockerIcons.Container, typeof(DockerContainersPage), 100);
		Add(services, DockerPageIds.Stats, "Stats", DockerIcons.Stats, typeof(DockerStatsPage), 200);
		Add(services, DockerPageIds.Compose, "Compose", DockerIcons.Compose, typeof(DockerComposePage), 300);
		Add(services, DockerPageIds.Images, "Images", DockerIcons.Image, typeof(DockerImagesPage), 400);
		Add(services, DockerPageIds.Volumes, "Volumes", DockerIcons.Volume, typeof(DockerVolumesPage), 500);
		Add(services, DockerPageIds.Networks, "Networks", DockerIcons.Network, typeof(DockerNetworksPage), 600);
		Add(services, DockerPageIds.Cleanup, "Cleanup", DockerIcons.Prune, typeof(DockerCleanupPage), 700);
	}

	private static void Add(IServiceCollection services, string id, string title, MokaIconDefinition icon, Type component, int order) =>
		services.AddDockerPage(new DockerPageDescriptor
		{
			Id = id,
			Title = title,
			Icon = icon,
			Component = component,
			Order = order,
		});
}
