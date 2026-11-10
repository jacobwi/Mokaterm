using System.Text;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Maui.Services;
using Mokaterm.UI.Presentation;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

// What draws before the page can, the desktop window and the web hosts' splash, starts in the mode the settings ask for.
public sealed class StartupThemeTests
{
	[Theory]
	[InlineData("""{ "appearance": { "mode": "Light" } }""", true)]
	[InlineData("""{ "appearance": { "mode": "Dark" } }""", false)]
	[InlineData("""{ "Appearance": { "Mode": "light" } }""", true)]
	[InlineData("""{ "appearance": { "mode": 1 } }""", true)]
	[InlineData("""{ "appearance": { "mode": 0 } }""", false)]
	[InlineData("""{ "security": { "autoLockMinutes": 5 } }""", false)]
	[InlineData("""{ "appearance": { "accentColor": "#6E92FF" } }""", false)]
	[InlineData("""{ "appearance": { "mode": "Sepia" } }""", false)]
	[InlineData("""{ "appearance": "light" }""", false)]
	[InlineData("""[ "light" ]""", false)]
	[InlineData("not json", false)]
	[InlineData("", false)]
	public void TheSettingsFile_NamesTheMode_AndAnythingElseIsDark(string json, bool light)
	{
		Assert.Equal(light, SettingsFileMode.IsLight(Encoding.UTF8.GetBytes(json)));
	}

	// The settings service reads a hand-edited file leniently, so the early read must not disagree with it.
	[Fact]
	public void AHandEditedFile_WithCommentsTrailingCommasAndAByteOrderMark_StillReads()
	{
		string json = "{\n  // edited by hand\n  \"appearance\": { \"mode\": \"Light\", },\n}";
		byte[] bytes = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(json)];

		Assert.True(SettingsFileMode.IsLight(bytes));
	}

	[Fact]
	public void AMissingFile_IsDark()
	{
		Assert.False(SettingsFileMode.ReadIsLight(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json")));
	}

	[Theory]
	[InlineData(ThemeMode.Light, "light")]
	[InlineData(ThemeMode.Dark, "dark")]
	public async Task TheWebSplash_IsInTheStoredMode(ThemeMode mode, string attribute)
	{
		FixedSettings settings = new(new AppearanceSettings { Mode = mode });

		Assert.Equal(attribute, await BootTheme.ModeAsync(settings, TestContext.Current.CancellationToken));
	}
}
