using Microsoft.AspNetCore.Components;

namespace Mokaterm.Plugin.TestHost;

// Moka.Red's tabs, which ViewTabs draws, inject a NavigationManager; only a tab with an address ever navigates.
internal sealed class StaticNavigation : NavigationManager
{
	public StaticNavigation() => Initialize("http://localhost/", "http://localhost/");
}
