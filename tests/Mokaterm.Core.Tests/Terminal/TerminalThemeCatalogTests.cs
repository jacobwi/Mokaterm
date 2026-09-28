using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Storage;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Core.Terminal;
using Mokaterm.Core.Tests.TestSupport;

namespace Mokaterm.Core.Tests.Terminal;

public sealed partial class TerminalThemeCatalogTests
{
	private static readonly string[] BuiltInIds =
	[
		"moka-dark", "moka-light", "campbell", "one-dark", "dracula", "nord", "gruvbox-dark", "solarized-dark",
		"solarized-light", "monokai", "tokyo-night", "catppuccin-mocha", "github-dark", "github-light",
		"catppuccin-frappe", "catppuccin-macchiato", "catppuccin-latte", "rose-pine", "rose-pine-moon",
		"rose-pine-dawn", "kanagawa-wave", "everforest-dark", "ayu-dark", "ayu-mirage", "tomorrow-night",
		"night-owl", "gruvbox-light", "one-light",
	];

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Themes_ListBuiltInsWithMokaDarkAsDefault()
	{
		await using CoreTestContext context = CreateContext();
		ITerminalThemeCatalog catalog = Catalog(context);

		Assert.Equal(BuiltInIds, catalog.Themes.Select(theme => theme.Id).ToArray());
		Assert.Equal("moka-dark", catalog.Default.Id);
		Assert.All(catalog.Themes, theme => Assert.False(theme.IsCustom));
	}

	[Fact]
	public async Task Default_UsesMokaPalette()
	{
		await using CoreTestContext context = CreateContext();
		TerminalTheme theme = Catalog(context).Default;

		Assert.Equal("#060608", theme.Background);
		Assert.Equal("#e8e8ec", theme.Foreground);
		Assert.Equal("#ef5350", theme.Cursor);
		Assert.Equal("#ef535040", theme.SelectionBackground);
		Assert.Equal("#00e676", theme.Green);
		Assert.True(theme.IsDark);
	}

	[Fact]
	public async Task BuiltInThemes_UseHexColors()
	{
		await using CoreTestContext context = CreateContext();

		foreach (TerminalTheme theme in Catalog(context).Themes)
		{
			foreach (string color in Colors(theme))
			{
				Assert.Matches(HexColor(), color);
			}
		}
	}

	[Fact]
	public async Task BuiltInThemes_PassTheCustomThemeRules()
	{
		await using CoreTestContext context = CreateContext();

		Assert.All(Catalog(context).Themes, theme => Assert.True(TerminalThemeRules.IsComplete(theme), theme.Id));
	}

	[Fact]
	public async Task BuiltInThemes_HaveAsciiNamesAndAMatchingDarkFlag()
	{
		await using CoreTestContext context = CreateContext();

		foreach (TerminalTheme theme in Catalog(context).Themes)
		{
			Assert.All(theme.Name, character => Assert.InRange(character, ' ', '~'));
			Assert.Equal(theme.Name.Trim(), theme.Name);
			Assert.Equal(TerminalColors.IsDark(theme.Background), theme.IsDark);
		}
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("no-such-theme")]
	public async Task Get_UnknownId_ReturnsDefault(string? id)
	{
		await using CoreTestContext context = CreateContext();
		ITerminalThemeCatalog catalog = Catalog(context);

		Assert.Same(catalog.Default, catalog.Get(id));
	}

	[Fact]
	public async Task Get_IgnoresCase()
	{
		await using CoreTestContext context = CreateContext();

		Assert.Equal("dracula", Catalog(context).Get("Dracula").Id);
	}

	[Fact]
	public async Task CustomThemes_FollowBuiltInsAndCannotShadowThem()
	{
		await using CoreTestContext context = CreateContext();
		ITerminalThemeCatalog catalog = Catalog(context);
		ISettingsService settings = context.Services.GetRequiredService<ISettingsService>();
		TerminalTheme shadow = CustomTheme("dracula") with { Background = "#123456" };

		await settings.UpdateAsync<TerminalSettings>(terminal => terminal with { CustomThemes = [CustomTheme("mine"), shadow] }, Ct);

		Assert.Equal(BuiltInIds.Length + 1, catalog.Themes.Count);
		TerminalTheme mine = catalog.Themes[^1];
		Assert.Equal("mine", mine.Id);
		Assert.True(mine.IsCustom);
		Assert.Same(mine, catalog.Get("mine"));
		Assert.Equal("#282a36", catalog.Get("dracula").Background);
	}

	[Fact]
	public async Task SettingsChanges_RebuildCatalog()
	{
		await using CoreTestContext context = CreateContext();
		ITerminalThemeCatalog catalog = Catalog(context);
		ISettingsService settings = context.Services.GetRequiredService<ISettingsService>();
		await settings.UpdateAsync<TerminalSettings>(terminal => terminal with { CustomThemes = [CustomTheme("mine")] }, Ct);

		await settings.ResetAsync<TerminalSettings>(Ct);

		Assert.Equal(BuiltInIds.Length, catalog.Themes.Count);
		Assert.Same(catalog.Default, catalog.Get("mine"));
	}

	internal static TerminalTheme CustomTheme(string id) => new()
	{
		Id = id,
		Name = "Custom " + id,
		Foreground = "#ffffff",
		Background = "#000000",
		Cursor = "#ff0000",
		CursorAccent = "#000000",
		SelectionBackground = "#ffffff40",
		Black = "#000000",
		Red = "#ff0000",
		Green = "#00ff00",
		Yellow = "#ffff00",
		Blue = "#0000ff",
		Magenta = "#ff00ff",
		Cyan = "#00ffff",
		White = "#ffffff",
		BrightBlack = "#808080",
		BrightRed = "#ff8080",
		BrightGreen = "#80ff80",
		BrightYellow = "#ffff80",
		BrightBlue = "#8080ff",
		BrightMagenta = "#ff80ff",
		BrightCyan = "#80ffff",
		BrightWhite = "#ffffff",
	};

	private static CoreTestContext CreateContext() =>
		new(services => services.AddSingleton<IAppDataStore>(new InMemoryAppDataStore()));

	private static ITerminalThemeCatalog Catalog(CoreTestContext context) => context.Services.GetRequiredService<ITerminalThemeCatalog>();

	private static IEnumerable<string> Colors(TerminalTheme theme) =>
	[
		theme.Foreground, theme.Background, theme.Cursor, theme.CursorAccent, theme.SelectionBackground,
		theme.Black, theme.Red, theme.Green, theme.Yellow, theme.Blue, theme.Magenta, theme.Cyan, theme.White,
		theme.BrightBlack, theme.BrightRed, theme.BrightGreen, theme.BrightYellow, theme.BrightBlue, theme.BrightMagenta, theme.BrightCyan, theme.BrightWhite,
	];

	[GeneratedRegex("^#[0-9a-f]{6}([0-9a-f]{2})?$")]
	private static partial Regex HexColor();
}
