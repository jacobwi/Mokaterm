using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Core.Tests.Abstractions;

public sealed class TerminalSettingsTests
{
	private static readonly TerminalSettings Settings = new()
	{
		ThemeId = "moka-dark",
		ProductionThemeId = "one-dark",
		StagingThemeId = "  ",
	};

	[Theory]
	[InlineData(HostEnvironment.Production, "one-dark")]
	[InlineData(HostEnvironment.Staging, "moka-dark")]
	[InlineData(HostEnvironment.Development, "moka-dark")]
	[InlineData(HostEnvironment.None, "moka-dark")]
	public void ForEnvironment_UsesTheThemeSetForThatEnvironment(HostEnvironment environment, string expected) =>
		Assert.Equal(expected, Settings.ForEnvironment(environment).ThemeId);

	[Fact]
	public void ForEnvironment_LeavesEverythingElseAlone()
	{
		TerminalSettings production = Settings.ForEnvironment(HostEnvironment.Production);

		Assert.Equal(Settings with { ThemeId = production.ThemeId }, production);
	}

	[Fact]
	public void WithOverrides_BeatsTheEnvironmentTheme()
	{
		TerminalProfileOverrides overrides = new() { ThemeId = "dracula" };

		Assert.Equal("dracula", Settings.ForEnvironment(HostEnvironment.Production).WithOverrides(overrides).ThemeId);
	}

	// The connection editor used to allow 6 to 72 for a login's own font size while the settings page allowed 6 to 48,
	// and the override was applied without going near a range at all.
	[Fact]
	public void WithOverrides_HoldsAFontSizeToTheRangeTheSettingsPageOffers()
	{
		TerminalSettings settings = new() { FontSize = 13 };

		Assert.Equal(TerminalSettings.MaxFontSize, settings.WithOverrides(new TerminalProfileOverrides { FontSize = 72 }).FontSize);
		Assert.Equal(TerminalSettings.MinFontSize, settings.WithOverrides(new TerminalProfileOverrides { FontSize = 0 }).FontSize);
		Assert.Equal(20, settings.WithOverrides(new TerminalProfileOverrides { FontSize = 20 }).FontSize);
	}

	[Fact]
	public void WithOverrides_WithAFontSizeThatIsNotANumber_KeepsTheGlobalOne()
	{
		TerminalSettings settings = new() { FontSize = 13 };

		Assert.Equal(13, settings.WithOverrides(new TerminalProfileOverrides { FontSize = double.NaN }).FontSize);
	}

	[Fact]
	public void ThemeIdFor_TreatsBlankAsUnset()
	{
		Assert.Null(Settings.ThemeIdFor(HostEnvironment.Staging));
		Assert.Equal("one-dark", Settings.ThemeIdFor(HostEnvironment.Production));
	}
}
