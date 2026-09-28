using Mokaterm.Abstractions.Presentation;

namespace Mokaterm.Core.Tests.Abstractions;

/// <summary>
/// One validator for every hex color in the app. There used to be three, and they disagreed: an accent of #abc was kept
/// by the settings sanitizer and drawn by the theme while the color field refused to save it.
/// </summary>
public sealed class HexColorTests
{
	[Theory]
	[InlineData("#abc", true)]
	[InlineData("#AABBCC", true)]
	[InlineData("#aabbccdd", false)]
	[InlineData("#ab", false)]
	[InlineData("#abcd", false)]
	[InlineData("aabbcc", false)]
	[InlineData("#gggggg", false)]
	[InlineData(" #aabbcc", false)]
	[InlineData("", false)]
	[InlineData(null, false)]
	public void IsValid_ForAnAccent_TakesTheShortAndTheLongFormOnly(string? value, bool expected) =>
		Assert.Equal(expected, HexColor.IsValid(value, HexColorForms.Accent));

	[Theory]
	[InlineData("#aabbcc", true)]
	[InlineData("#aabbcc80", true)]
	[InlineData("#abc", false)]
	public void IsValid_ForAThemeSlot_TakesAlphaAndNotTheShortForm(string value, bool expected) =>
		Assert.Equal(expected, HexColor.IsValid(value, HexColorForms.ThemeSlot));

	[Theory]
	[InlineData(" #abc ", "#aabbcc")]
	[InlineData("#AbC", "#AAbbCC")]
	[InlineData("#12345", null)]
	[InlineData("   ", null)]
	[InlineData(null, null)]
	public void Normalize_ExpandsTheShortForm_AndKeepsTheCaseItWasWrittenIn(string? value, string? expected) =>
		Assert.Equal(expected, HexColor.Normalize(value, HexColorForms.Accent));

	[Fact]
	public void Normalize_LeavesAColorThatIsAlreadyStoredAlone()
	{
		Assert.Equal("#ef5350", HexColor.Normalize("#ef5350", HexColorForms.Accent));
		Assert.Equal("#ef535080", HexColor.Normalize("#ef535080", HexColorForms.ThemeSlot));
	}

	[Fact]
	public void Requirement_NamesExactlyTheFormsItTakes()
	{
		Assert.Equal("Use #rgb or #rrggbb.", HexColor.Requirement(HexColorForms.Accent));
		Assert.Equal("Use #rrggbb or #rrggbbaa.", HexColor.Requirement(HexColorForms.ThemeSlot));
		Assert.Equal("Use #rrggbb.", HexColor.Requirement(HexColorForms.Opaque));
	}
}
