using Mokaterm.Modules.Rdp.Protocol;

namespace Mokaterm.Modules.Rdp.Tests;

public sealed class RdpKeyboardMapTests
{
	[Theory]
	[InlineData("KeyA", 0x1E)]
	[InlineData("KeyM", 0x32)]
	[InlineData("KeyQ", 0x10)]
	[InlineData("KeyZ", 0x2C)]
	[InlineData("Digit1", 0x02)]
	[InlineData("Digit0", 0x0B)]
	[InlineData("Space", 0x39)]
	[InlineData("Enter", 0x1C)]
	[InlineData("Tab", 0x0F)]
	[InlineData("Escape", 0x01)]
	[InlineData("Backspace", 0x0E)]
	public void TryGet_MapsTheMainBlock(string code, int scancode)
	{
		Assert.True(RdpKeyboardMap.TryGet(code, out RdpScancode result));

		Assert.False(result.Extended);
		Assert.Equal(scancode, result.Code);
	}

	[Theory]
	[InlineData("F1", 0x3B)]
	[InlineData("F10", 0x44)]
	[InlineData("F11", 0x57)]
	[InlineData("F12", 0x58)]
	public void TryGet_MapsTheFunctionKeys(string code, int scancode)
	{
		Assert.True(RdpKeyboardMap.TryGet(code, out RdpScancode result));

		Assert.False(result.Extended);
		Assert.Equal(scancode, result.Code);
	}

	[Theory]
	[InlineData("Minus", 0x0C)]
	[InlineData("Equal", 0x0D)]
	[InlineData("BracketLeft", 0x1A)]
	[InlineData("BracketRight", 0x1B)]
	[InlineData("Semicolon", 0x27)]
	[InlineData("Quote", 0x28)]
	[InlineData("Backquote", 0x29)]
	[InlineData("Backslash", 0x2B)]
	[InlineData("Comma", 0x33)]
	[InlineData("Period", 0x34)]
	[InlineData("Slash", 0x35)]
	[InlineData("IntlBackslash", 0x56)]
	public void TryGet_MapsThePunctuation(string code, int scancode)
	{
		Assert.True(RdpKeyboardMap.TryGet(code, out RdpScancode result));

		Assert.False(result.Extended);
		Assert.Equal(scancode, result.Code);
	}

	[Theory]
	[InlineData("ShiftLeft", 0x2A)]
	[InlineData("ShiftRight", 0x36)]
	[InlineData("ControlLeft", 0x1D)]
	[InlineData("AltLeft", 0x38)]
	[InlineData("CapsLock", 0x3A)]
	[InlineData("NumLock", 0x45)]
	[InlineData("ScrollLock", 0x46)]
	public void TryGet_MapsTheModifiersThatAreNotExtended(string code, int scancode)
	{
		Assert.True(RdpKeyboardMap.TryGet(code, out RdpScancode result));

		Assert.False(result.Extended);
		Assert.Equal(scancode, result.Code);
	}

	[Theory]
	[InlineData("ControlRight", 0x1D)]
	[InlineData("AltRight", 0x38)]
	[InlineData("MetaLeft", 0x5B)]
	[InlineData("MetaRight", 0x5C)]
	[InlineData("ContextMenu", 0x5D)]
	public void TryGet_MarksTheRightHandModifiersExtended(string code, int scancode)
	{
		Assert.True(RdpKeyboardMap.TryGet(code, out RdpScancode result));

		Assert.True(result.Extended);
		Assert.Equal(scancode, result.Code);
	}

	[Theory]
	[InlineData("Home", 0x47)]
	[InlineData("ArrowUp", 0x48)]
	[InlineData("PageUp", 0x49)]
	[InlineData("ArrowLeft", 0x4B)]
	[InlineData("ArrowRight", 0x4D)]
	[InlineData("End", 0x4F)]
	[InlineData("ArrowDown", 0x50)]
	[InlineData("PageDown", 0x51)]
	[InlineData("Insert", 0x52)]
	[InlineData("Delete", 0x53)]
	public void TryGet_MarksTheNavigationBlockExtended(string code, int scancode)
	{
		Assert.True(RdpKeyboardMap.TryGet(code, out RdpScancode result));

		Assert.True(result.Extended);
		Assert.Equal(scancode, result.Code);
	}

	[Theory]
	[InlineData("Numpad0", 0x52)]
	[InlineData("Numpad1", 0x4F)]
	[InlineData("Numpad7", 0x47)]
	[InlineData("NumpadDecimal", 0x53)]
	[InlineData("NumpadAdd", 0x4E)]
	[InlineData("NumpadSubtract", 0x4A)]
	[InlineData("NumpadMultiply", 0x37)]
	public void TryGet_LeavesTheKeypadUnextended(string code, int scancode)
	{
		Assert.True(RdpKeyboardMap.TryGet(code, out RdpScancode result));

		Assert.False(result.Extended);
		Assert.Equal(scancode, result.Code);
	}

	[Theory]
	[InlineData("NumpadEnter", 0x1C)]
	[InlineData("NumpadDivide", 0x35)]
	public void TryGet_ExtendsTheKeypadKeysThatShareAScancode(string code, int scancode)
	{
		Assert.True(RdpKeyboardMap.TryGet(code, out RdpScancode result));

		Assert.True(result.Extended);
		Assert.Equal(scancode, result.Code);
	}

	[Theory]
	[InlineData("Numpad0", "Insert")]
	[InlineData("Numpad1", "End")]
	[InlineData("NumpadEnter", "Enter")]
	[InlineData("NumpadDivide", "Slash")]
	public void TryGet_TellsTheKeypadApartFromTheKeysItShadows(string keypad, string main)
	{
		Assert.True(RdpKeyboardMap.TryGet(keypad, out RdpScancode first));
		Assert.True(RdpKeyboardMap.TryGet(main, out RdpScancode second));

		Assert.NotEqual(first.Value, second.Value);
	}

	[Fact]
	public void Value_PutsTheExtendedFlagInTheHighByte()
	{
		Assert.True(RdpKeyboardMap.TryGet("ArrowLeft", out RdpScancode left));
		Assert.True(RdpKeyboardMap.TryGet("KeyA", out RdpScancode a));

		Assert.Equal(0xE04B, left.Value);
		Assert.Equal(0x001E, a.Value);
	}

	[Fact]
	public void TryGet_TreatsTheWindowsKeyAliasesAsTheSameKey()
	{
		Assert.True(RdpKeyboardMap.TryGet("MetaLeft", out RdpScancode meta));
		Assert.True(RdpKeyboardMap.TryGet("OSLeft", out RdpScancode os));

		Assert.Equal(meta, os);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("AudioVolumeUp")]
	[InlineData("BrowserBack")]
	[InlineData("keya")]
	[InlineData("F25")]
	public void TryGet_HasNoScancodeForKeysRdpDoesNotCarry(string? code)
	{
		Assert.False(RdpKeyboardMap.TryGet(code, out RdpScancode result));

		Assert.Equal(default, result);
	}

	[Fact]
	public void KnownCodes_HasOneScancodePerKey()
	{
		List<RdpScancode> scancodes = [];
		foreach (string code in RdpKeyboardMap.KnownCodes)
		{
			Assert.True(RdpKeyboardMap.TryGet(code, out RdpScancode scancode));
			scancodes.Add(scancode);
		}

		// Only the two Windows key aliases may repeat a scancode.
		Assert.Equal(scancodes.Count - 2, scancodes.Distinct().Count());
		Assert.All(scancodes, scancode => Assert.NotEqual(0, scancode.Code));
	}

	[Fact]
	public void KnownCodes_CoversTheLettersAndTheDigits()
	{
		for (char letter = 'A'; letter <= 'Z'; letter++)
		{
			Assert.True(RdpKeyboardMap.TryGet($"Key{letter}", out _), $"Key{letter} has no scancode.");
		}

		for (int digit = 0; digit <= 9; digit++)
		{
			Assert.True(RdpKeyboardMap.TryGet($"Digit{digit}", out _), $"Digit{digit} has no scancode.");
			Assert.True(RdpKeyboardMap.TryGet($"Numpad{digit}", out _), $"Numpad{digit} has no scancode.");
		}

		for (int number = 1; number <= 12; number++)
		{
			Assert.True(RdpKeyboardMap.TryGet($"F{number}", out _), $"F{number} has no scancode.");
		}
	}
}
