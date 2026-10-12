using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Storage;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Core.Plugins;
using Mokaterm.Core.Terminal;
using Mokaterm.Core.Tests.Terminal;
using Mokaterm.Core.Tests.TestSupport;

namespace Mokaterm.Core.Tests.Plugins;

public sealed class PluginThemeTests
{
	[Fact]
	public async Task PluginThemes_FollowTheBuiltIns_WithAnIdThatSurvivesARestart()
	{
		await using CoreTestContext context = new(services => services.AddSingleton<IAppDataStore>(new InMemoryAppDataStore()));
		PluginThemeSet set = new([
			new PluginThemeFile("moka.themes", "Dracula Pro.json", TerminalThemeSamples.WindowsTerminalDracula),
			new PluginThemeFile("moka.themes", "broken.json", "{ not a scheme"),
		]);

		using TerminalThemeCatalog catalog = new(context.Services.GetRequiredService<ISettingsService>(), new TerminalThemeConverter(), set);

		TerminalTheme theme = catalog.Get("plugin.moka.themes.dracula-pro");
		Assert.Equal("plugin.moka.themes.dracula-pro", theme.Id);
		Assert.Equal("Dracula", theme.Name);
		Assert.False(theme.IsCustom);
		Assert.Equal(BuiltInTerminalThemes.All.Count + 1, catalog.Themes.Count);
	}
}
