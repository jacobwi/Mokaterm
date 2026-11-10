using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Data.FileBrowser;
using Mokaterm.UI.FileBrowser.Browsing;
using Mokaterm.UI.FileBrowser.Extensions;

namespace Mokaterm.UI.FileBrowser.Tests;

/// <summary>The browser's view (list or tiles, the sort, the details pane) is kept per window or circuit, never shared wider.</summary>
public sealed class FileBrowserViewMemoryTests
{
	[Fact]
	public async Task EachWindow_KeepsItsOwnView()
	{
		ServiceCollection services = new();
		services.AddMokatermFileBrowser();
		await using ServiceProvider provider = services.BuildServiceProvider();

		ServiceDescriptor memory = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(FileBrowserViewMemory));
		Assert.Equal(ServiceLifetime.Scoped, memory.Lifetime);

		await using AsyncServiceScope first = provider.CreateAsyncScope();
		await using AsyncServiceScope second = provider.CreateAsyncScope();
		FileBrowserViewMemory window = first.ServiceProvider.GetRequiredService<FileBrowserViewMemory>();
		window.View = window.View with { Mode = MokaFileViewMode.Tiles, SortBy = MokaFileSort.Size };
		window.DetailsOpen = true;

		Assert.Same(window, first.ServiceProvider.GetRequiredService<FileBrowserViewMemory>());
		FileBrowserViewMemory other = second.ServiceProvider.GetRequiredService<FileBrowserViewMemory>();
		Assert.Equal(MokaFileViewMode.List, other.View.Mode);
		Assert.Equal(MokaFileSort.Name, other.View.SortBy);
		Assert.False(other.DetailsOpen);
	}
}
