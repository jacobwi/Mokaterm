using System.Text;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Core.Tests.Abstractions;

public sealed class TerminalEncodingsTests
{
	[Fact]
	public void Resolve_UnknownName_FallsBackToUtf8()
	{
		Assert.False(TerminalEncodings.TryGet("not-a-character-set", out _));
		Assert.Equal(Encoding.UTF8.CodePage, TerminalEncodings.Resolve("not-a-character-set").CodePage);
		Assert.Equal(Encoding.UTF8.CodePage, TerminalEncodings.Resolve(" ").CodePage);
		Assert.Equal(Encoding.UTF8.CodePage, TerminalEncodings.Resolve(null).CodePage);
	}

	[Fact]
	public void Resolve_CodePageName_IsKnownAfterTheProviderIsRegistered()
	{
		Assert.True(TerminalEncodings.TryGet("ibm437", out Encoding? encoding));
		Assert.Equal(437, encoding.CodePage);
		Assert.False(TerminalEncodings.IsUtf8(encoding));
	}

	[Fact]
	public void TryGet_PaddedName_IsTrimmed()
	{
		Assert.True(TerminalEncodings.TryGet("  windows-1252  ", out Encoding? encoding));
		Assert.Equal(1252, encoding.CodePage);
	}

	[Fact]
	public void TryGet_ACodePage_SubstitutesInsteadOfThrowingBothWays()
	{
		Assert.True(TerminalEncodings.TryGet("us-ascii", out Encoding? encoding));

		// A damaged byte and a character the line cannot carry both have to survive as something.
		Assert.NotEmpty(encoding.GetString([0xFF]));
		Assert.Equal([(byte)'?'], encoding.GetBytes("é"));
	}

	[Fact]
	public void Default_IsUtf8_WhichIsWhatThePassthroughPathChecks()
	{
		Assert.True(TerminalEncodings.IsUtf8(TerminalEncodings.Resolve(TerminalEncodings.Default)));
		Assert.True(TerminalEncodings.IsUtf8(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)));
	}
}
