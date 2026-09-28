using Mokaterm.Abstractions.Presentation;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.UI.Tests;

/// <summary>
/// What the color field saves. It used to refuse the short form the settings sanitizer kept and the theme applied, so an
/// accent of #abc could sit in settings.json, paint the whole app, and still be rejected by the field showing it.
/// </summary>
public sealed class ColorSettingTests
{
	[Fact]
	public void Parse_AnAccentWrittenShort_IsSavedInTheFormEverythingStores()
	{
		Assert.Equal("#aabbcc", ColorSetting.Parse("#abc", HexColorForms.Accent, allowEmpty: false));
		Assert.Equal("#ef5350", ColorSetting.Parse("  #ef5350  ", HexColorForms.Accent, allowEmpty: false));
	}

	[Fact]
	public void Parse_WhatTheAccentSettingKeeps_IsAlwaysSavable()
	{
		// The two sides of the disagreement, checked against each other: anything the stored value can hold, the field
		// that edits it must take.
		foreach (string typed in new[] { "#abc", "#ABC", "#a1b2c3", "#ef5350" })
		{
			string stored = new AppearanceSettings { AccentColor = typed }.Clamped().AccentColor;

			Assert.Equal(stored, ColorSetting.Parse(typed, HexColorForms.Accent, allowEmpty: false));
		}
	}

	[Fact]
	public void Parse_AlphaAndEmptyText_OnlyWhereTheFieldAllowsThem()
	{
		Assert.Equal("#ef535080", ColorSetting.Parse("#ef535080", HexColorForms.ThemeSlot, allowEmpty: false));
		Assert.Null(ColorSetting.Parse("#ef535080", HexColorForms.Opaque, allowEmpty: false));
		Assert.Equal("", ColorSetting.Parse("   ", HexColorForms.Opaque, allowEmpty: true));
		Assert.Null(ColorSetting.Parse("   ", HexColorForms.Opaque, allowEmpty: false));
	}

	[Fact]
	public void Parse_TextThatIsNotAColor_SavesNothing()
	{
		Assert.Null(ColorSetting.Parse("red", HexColorForms.Accent, allowEmpty: true));
		Assert.Null(ColorSetting.Parse("#12345", HexColorForms.Accent, allowEmpty: true));
	}
}
