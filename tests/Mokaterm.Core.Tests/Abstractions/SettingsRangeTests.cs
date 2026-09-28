using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Core.Tests.Abstractions;

/// <summary>
/// The ranges themselves, written down once here as literals: each of these used to live in two places at once, and
/// three pairs had already drifted apart. A change to one of them should fail here first and be a decision, not an
/// accident.
/// </summary>
public sealed class SettingsRangeTests
{
	[Fact]
	public void TheFontScaleRange_IsWhatTheSliderOffersAndTheFileKeeps()
	{
		// The wider stored range won: the app applies 0.75 and 1.5 perfectly well, so the slider offers all of it.
		Assert.Equal(0.75, AppearanceSettings.MinFontScale);
		Assert.Equal(1.5, AppearanceSettings.MaxFontScale);
	}

	[Fact]
	public void TheTerminalRanges_AreTheNarrowerVisibleOnes()
	{
		Assert.Equal(6, TerminalSettings.MinFontSize);
		Assert.Equal(48, TerminalSettings.MaxFontSize);

		// The page offered 1 to 2 while the file kept 0.8 to 3.
		Assert.Equal(1, TerminalSettings.MinLineHeight);
		Assert.Equal(2, TerminalSettings.MaxLineHeight);

		// The page offered 0 to 10 while the file only refused NaN.
		Assert.Equal(0, TerminalSettings.MinLetterSpacing);
		Assert.Equal(10, TerminalSettings.MaxLetterSpacing);

		// The page offered 100000 lines while the file kept 200000.
		Assert.Equal(0, TerminalSettings.MinScrollback);
		Assert.Equal(100_000, TerminalSettings.MaxScrollback);

		Assert.Equal(1, TerminalSettings.MinContrastRatio);
		Assert.Equal(21, TerminalSettings.MaxContrastRatio);
	}

	[Fact]
	public void TheRemainingRanges_AreWhatBothSidesAlreadyAgreedOn()
	{
		Assert.Equal(0, SecuritySettings.MinAutoLockMinutes);
		Assert.Equal(1440, SecuritySettings.MaxAutoLockMinutes);
		Assert.Equal(0, SecuritySettings.MinClipboardClearSeconds);
		Assert.Equal(600, SecuritySettings.MaxClipboardClearSeconds);
		Assert.Equal(1, FileTransferSettings.MinParallelTransfers);
		Assert.Equal(10, FileTransferSettings.MaxParallelTransfers);
		Assert.Equal(1, GeneralSettings.MinAutoReconnectDelaySeconds);
		Assert.Equal(300, GeneralSettings.MaxAutoReconnectDelaySeconds);
		Assert.Equal(0, GeneralSettings.MinRecentConnectionsLimit);
		Assert.Equal(50, GeneralSettings.MaxRecentConnectionsLimit);
	}

	[Fact]
	public void Clamped_KeepsTheEdgesAndPullsInWhatIsOutside()
	{
		TerminalSettings low = new TerminalSettings
		{
			FontSize = TerminalSettings.MinFontSize,
			LineHeight = TerminalSettings.MinLineHeight,
			Scrollback = TerminalSettings.MinScrollback,
		}.Clamped();
		Assert.Equal(TerminalSettings.MinFontSize, low.FontSize);
		Assert.Equal(TerminalSettings.MinLineHeight, low.LineHeight);
		Assert.Equal(TerminalSettings.MinScrollback, low.Scrollback);

		TerminalSettings beyond = new TerminalSettings
		{
			FontSize = 500,
			LineHeight = 0.1,
			LetterSpacing = -4,
			Scrollback = 10_000_000,
			MinimumContrastRatio = 99,
		}.Clamped();
		Assert.Equal(TerminalSettings.MaxFontSize, beyond.FontSize);
		Assert.Equal(TerminalSettings.MinLineHeight, beyond.LineHeight);
		Assert.Equal(TerminalSettings.MinLetterSpacing, beyond.LetterSpacing);
		Assert.Equal(TerminalSettings.MaxScrollback, beyond.Scrollback);
		Assert.Equal(TerminalSettings.MaxContrastRatio, beyond.MinimumContrastRatio);
	}

	// Math.Clamp passes NaN through, and a settings.json can hold one.
	[Fact]
	public void Clamped_ReplacesValuesThatAreNotNumbersWithTheDefaults()
	{
		TerminalSettings terminal = new TerminalSettings
		{
			FontSize = double.NaN,
			LineHeight = double.PositiveInfinity,
			LetterSpacing = double.NaN,
			MinimumContrastRatio = double.NaN,
		}.Clamped();

		Assert.Equal(TerminalSettings.DefaultFontSize, terminal.FontSize);
		Assert.Equal(TerminalSettings.DefaultLineHeight, terminal.LineHeight);
		Assert.Equal(TerminalSettings.MinLetterSpacing, terminal.LetterSpacing);
		Assert.Equal(TerminalSettings.MinContrastRatio, terminal.MinimumContrastRatio);
		Assert.Equal(AppearanceSettings.DefaultFontScale, new AppearanceSettings { FontScale = double.NaN }.Clamped().FontScale);
	}

	[Fact]
	public void Clamped_NormalizesTheAccentColorAndRefusesWhatIsNotOne()
	{
		Assert.Equal("#aabbcc", new AppearanceSettings { AccentColor = " #abc " }.Clamped().AccentColor);
		Assert.Equal(AppearanceSettings.DefaultAccentColor, new AppearanceSettings { AccentColor = "red" }.Clamped().AccentColor);

		// Alpha belongs to the terminal theme slots, never to the accent: a translucent accent would draw the whole UI
		// half transparent.
		Assert.Equal(AppearanceSettings.DefaultAccentColor, new AppearanceSettings { AccentColor = "#ef535080" }.Clamped().AccentColor);
	}

	[Fact]
	public void Clamped_ChangesNothingWhenEverythingIsInRange()
	{
		TerminalSettings terminal = new();
		SecuritySettings security = new();
		GeneralSettings general = new();

		// Record equality is what tells the settings service an update changed nothing, so clamping a value that was
		// already fine has to come back equal.
		Assert.Equal(terminal, terminal.Clamped());
		Assert.Equal(security, security.Clamped());
		Assert.Equal(general, general.Clamped());
		Assert.Equal(new AppearanceSettings(), new AppearanceSettings().Clamped());
		Assert.Equal(new FileTransferSettings(), new FileTransferSettings().Clamped());
	}
}
