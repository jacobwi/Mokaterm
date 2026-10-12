using System.Text;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Core.Tests.Abstractions;

public sealed class Utf8CutTests
{
	[Theory]
	[InlineData(new byte[] { 0x61, 0x62 }, 2)]
	[InlineData(new byte[] { 0x61, 0xC3 }, 1)]
	[InlineData(new byte[] { 0x61, 0xC3, 0xA9 }, 3)]
	[InlineData(new byte[] { 0x61, 0xE2, 0x82 }, 1)]
	[InlineData(new byte[] { 0xF0, 0x9F, 0x98 }, 0)]
	[InlineData(new byte[] { 0xF0, 0x9F, 0x98, 0x80 }, 4)]
	[InlineData(new byte[] { 0x61, 0xFF }, 2)]
	[InlineData(new byte[] { }, 0)]
	public void CompleteLength_DropsOnlyACharacterTheCapCutInHalf(byte[] bytes, int expected) =>
		Assert.Equal(expected, Utf8Cut.CompleteLength(bytes));

	[Fact]
	public void CapturedOutput_ACutStream_LosesOnlyTheSplitCharacter()
	{
		byte[] bytes = [.. Encoding.UTF8.GetBytes("a" + (char)0x20AC), 0xF0, 0x9F];

		Assert.Equal("a" + (char)0x20AC, new CapturedOutput(bytes, 100).ToText());
	}

	[Fact]
	public void CapturedOutput_AWholeStream_IsDecodedAsItCame()
	{
		// Not cut, so a broken end is the command's own and shows as a replacement mark.
		Assert.Equal("a" + (char)0xFFFD, new CapturedOutput(new byte[] { 0x61, 0xC3 }, 2).ToText());
	}
}
