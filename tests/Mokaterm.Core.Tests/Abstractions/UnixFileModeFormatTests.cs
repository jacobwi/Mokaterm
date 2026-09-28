using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Core.Tests.Abstractions;

public sealed class UnixFileModeFormatTests
{
	[Theory]
	[InlineData("755", "755")]
	[InlineData("644", "644")]
	[InlineData("0", "000")]
	[InlineData("7", "007")]
	[InlineData("4755", "4755")]
	[InlineData("1777", "1777")]
	public void ToOctal_PadsToThreeOrFourDigits(string octal, string expected) =>
		Assert.Equal(expected, UnixFileModeFormat.ToOctal(Mode(octal)));

	[Theory]
	[InlineData("755", "rwxr-xr-x")]
	[InlineData("644", "rw-r--r--")]
	[InlineData("0", "---------")]
	[InlineData("4755", "rwsr-xr-x")]
	[InlineData("4644", "rwSr--r--")]
	[InlineData("2775", "rwxrwsr-x")]
	[InlineData("2765", "rwxrwSr-x")]
	[InlineData("1777", "rwxrwxrwt")]
	[InlineData("1776", "rwxrwxrwT")]
	public void ToSymbolic_MatchesLs(string octal, string expected) =>
		Assert.Equal(expected, UnixFileModeFormat.ToSymbolic(Mode(octal)));

	[Theory]
	[InlineData("644", "644")]
	[InlineData(" 755 ", "755")]
	[InlineData("4755", "4755")]
	[InlineData("0000", "0")]
	public void TryParseOctal_AcceptsThreeOrFourOctalDigits(string text, string expected)
	{
		Assert.True(UnixFileModeFormat.TryParseOctal(text, out UnixFileMode mode));
		Assert.Equal(Mode(expected), mode);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("75")]
	[InlineData("12345")]
	[InlineData("789")]
	[InlineData("rwx")]
	[InlineData("-644")]
	public void TryParseOctal_RejectsOtherText(string? text)
	{
		Assert.False(UnixFileModeFormat.TryParseOctal(text, out UnixFileMode mode));
		Assert.Equal(UnixFileMode.None, mode);
	}

	private static UnixFileMode Mode(string octal) => (UnixFileMode)Convert.ToInt32(octal, 8);
}
