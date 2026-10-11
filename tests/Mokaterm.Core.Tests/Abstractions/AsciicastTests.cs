using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Core.Tests.Abstractions;

public sealed class AsciicastTests
{
	[Fact]
	public void FormatSize_IsColumnsByRows() => Assert.Equal("120x32", Asciicast.FormatSize(120, 32));

	[Fact]
	public void TryParseSize_ReadsWhatFormatSizeWrites()
	{
		Assert.True(Asciicast.TryParseSize(Asciicast.FormatSize(80, 24), out int columns, out int rows));
		Assert.Equal(80, columns);
		Assert.Equal(24, rows);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("80")]
	[InlineData("x24")]
	[InlineData("80x")]
	[InlineData("0x24")]
	[InlineData("80x0")]
	[InlineData("-80x24")]
	[InlineData("80 x 24")]
	[InlineData("80x24x2")]
	[InlineData("99999999999x24")]
	public void TryParseSize_RefusesAnythingButTwoPositiveNumbers(string? value)
	{
		Assert.False(Asciicast.TryParseSize(value, out int columns, out int rows));
		Assert.Equal(0, columns);
		Assert.Equal(0, rows);
	}
}
