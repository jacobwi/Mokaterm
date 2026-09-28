using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Core.Tests.Abstractions;

public sealed class TerminalFontFamilyTests
{
	[Theory]
	[InlineData("monospace")]
	[InlineData("'JetBrains Mono', \"Cascadia Mono\", Consolas, monospace")]
	[InlineData("Fira Code, ui-monospace")]
	public void IsValid_AnOrdinaryFontList_IsAccepted(string fonts) => Assert.True(TerminalFontFamily.IsValid(fonts));

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("Consolas; color: red")]
	[InlineData("Consolas } body { display: none")]
	[InlineData("url(https://example.com/font)")]
	[InlineData("<script>")]
	[InlineData("Mono\\2c Space")]
	[InlineData("Mono\nSpace")]
	[InlineData("Mono\fSpace")]
	[InlineData("Mono\0Space")]
	public void IsValid_AListThatCouldLeaveTheDeclaration_IsRefused(string? fonts) => Assert.False(TerminalFontFamily.IsValid(fonts));

	[Fact]
	public void Strip_TakesOutEveryForbiddenCharacterAndTrims()
	{
		Assert.Equal("Consolas  body  display: none", TerminalFontFamily.Strip(" Consolas } body { display: none; "));
		Assert.Equal("", TerminalFontFamily.Strip("{}();\r\n"));
		Assert.Equal("", TerminalFontFamily.Strip(null));
	}

	[Fact]
	public void TheDefaultList_FollowsTheRule() => Assert.True(TerminalFontFamily.IsValid(new TerminalSettings().FontFamily));
}
