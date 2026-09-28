using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Core.Tests.Abstractions;

public sealed class TerminalLocalEchoTests
{
	[Fact]
	public void PlainCharacters_AreShownAsTyped() =>
		Assert.Equal("reboot"u8.ToArray(), TerminalLocalEcho.Build("reboot"u8));

	[Fact]
	public void Enter_BecomesAFullNewLine_SoTheNextLineIsNotDrawnOverThisOne() =>
		Assert.Equal("\r\n"u8.ToArray(), TerminalLocalEcho.Build([0x0D]));

	[Fact]
	public void PastedCrLf_IsOneNewLine() =>
		Assert.Equal("\r\n"u8.ToArray(), TerminalLocalEcho.Build([0x0D, 0x0A]));

	[Theory]
	[InlineData((byte)0x08)]
	[InlineData((byte)0x7F)]
	public void BothEraseKeys_TakeTheCharacterOffTheScreen(byte key) =>
		Assert.Equal("\b \b"u8.ToArray(), TerminalLocalEcho.Build([key]));

	[Fact]
	public void AMixedLine_IsTranslatedInPlace() =>
		Assert.Equal("ab\b \bc\r\n"u8.ToArray(), TerminalLocalEcho.Build([(byte)'a', (byte)'b', 0x7F, (byte)'c', 0x0D]));

	[Fact]
	public void ALineFeedOnItsOwn_IsShownAsItWasTyped() =>
		Assert.Equal("\n"u8.ToArray(), TerminalLocalEcho.Build([0x0A]));

	[Fact]
	public void Nothing_ShowsNothing() => Assert.Empty(TerminalLocalEcho.Build([]));
}
