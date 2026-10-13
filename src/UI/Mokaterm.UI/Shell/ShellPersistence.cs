using Microsoft.Extensions.DependencyInjection;

namespace Mokaterm.UI.Shell;

/// <summary>
/// For a host whose process ends right after its window closes: writes a page's layout state at once rather than after
/// the usual short delay, so what changed as the window closed (where its tool windows were) is kept.
/// </summary>
public static class ShellPersistence
{
	/// <param name="pageServices">The services of the page whose state to write.</param>
	public static Task FlushAsync(IServiceProvider pageServices)
	{
		ArgumentNullException.ThrowIfNull(pageServices);
		return pageServices.GetService<UiStateStore>()?.FlushAsync() ?? Task.CompletedTask;
	}
}
