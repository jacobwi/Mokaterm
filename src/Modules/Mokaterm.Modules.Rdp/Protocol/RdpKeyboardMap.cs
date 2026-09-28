using System.Collections.Frozen;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// Turns a browser <c>KeyboardEvent.code</c> into the scancode RDP expects. The code names a physical key, which
/// is exactly what a scancode is, so the table needs no knowledge of the layout on either side: the server maps
/// the scancode through its own layout.
/// </summary>
internal static class RdpKeyboardMap
{
	private static readonly FrozenDictionary<string, RdpScancode> Codes = Build();

	/// <summary>Every code this table knows, for tests and for the send-keys menu.</summary>
	public static IReadOnlyCollection<string> KnownCodes => Codes.Keys;

	/// <summary>False for a key this table has no scancode for, such as a media or browser key.</summary>
	public static bool TryGet(string? code, out RdpScancode scancode)
	{
		if (!string.IsNullOrEmpty(code))
		{
			return Codes.TryGetValue(code, out scancode);
		}

		scancode = default;
		return false;
	}

	private static FrozenDictionary<string, RdpScancode> Build()
	{
		Dictionary<string, RdpScancode> map = new(StringComparer.Ordinal);

		void Plain(string code, byte value) => map.Add(code, new RdpScancode(false, value));
		void Extended(string code, byte value) => map.Add(code, new RdpScancode(true, value));

		// The main block, in scancode order.
		Plain("Escape", 0x01);
		Plain("Digit1", 0x02);
		Plain("Digit2", 0x03);
		Plain("Digit3", 0x04);
		Plain("Digit4", 0x05);
		Plain("Digit5", 0x06);
		Plain("Digit6", 0x07);
		Plain("Digit7", 0x08);
		Plain("Digit8", 0x09);
		Plain("Digit9", 0x0A);
		Plain("Digit0", 0x0B);
		Plain("Minus", 0x0C);
		Plain("Equal", 0x0D);
		Plain("Backspace", 0x0E);
		Plain("Tab", 0x0F);
		Plain("KeyQ", 0x10);
		Plain("KeyW", 0x11);
		Plain("KeyE", 0x12);
		Plain("KeyR", 0x13);
		Plain("KeyT", 0x14);
		Plain("KeyY", 0x15);
		Plain("KeyU", 0x16);
		Plain("KeyI", 0x17);
		Plain("KeyO", 0x18);
		Plain("KeyP", 0x19);
		Plain("BracketLeft", 0x1A);
		Plain("BracketRight", 0x1B);
		Plain("Enter", 0x1C);
		Plain("ControlLeft", 0x1D);
		Plain("KeyA", 0x1E);
		Plain("KeyS", 0x1F);
		Plain("KeyD", 0x20);
		Plain("KeyF", 0x21);
		Plain("KeyG", 0x22);
		Plain("KeyH", 0x23);
		Plain("KeyJ", 0x24);
		Plain("KeyK", 0x25);
		Plain("KeyL", 0x26);
		Plain("Semicolon", 0x27);
		Plain("Quote", 0x28);
		Plain("Backquote", 0x29);
		Plain("ShiftLeft", 0x2A);
		Plain("Backslash", 0x2B);
		Plain("KeyZ", 0x2C);
		Plain("KeyX", 0x2D);
		Plain("KeyC", 0x2E);
		Plain("KeyV", 0x2F);
		Plain("KeyB", 0x30);
		Plain("KeyN", 0x31);
		Plain("KeyM", 0x32);
		Plain("Comma", 0x33);
		Plain("Period", 0x34);
		Plain("Slash", 0x35);
		Plain("ShiftRight", 0x36);
		Plain("NumpadMultiply", 0x37);
		Plain("AltLeft", 0x38);
		Plain("Space", 0x39);
		Plain("CapsLock", 0x3A);
		Plain("F1", 0x3B);
		Plain("F2", 0x3C);
		Plain("F3", 0x3D);
		Plain("F4", 0x3E);
		Plain("F5", 0x3F);
		Plain("F6", 0x40);
		Plain("F7", 0x41);
		Plain("F8", 0x42);
		Plain("F9", 0x43);
		Plain("F10", 0x44);
		Plain("NumLock", 0x45);
		Plain("ScrollLock", 0x46);
		Plain("Numpad7", 0x47);
		Plain("Numpad8", 0x48);
		Plain("Numpad9", 0x49);
		Plain("NumpadSubtract", 0x4A);
		Plain("Numpad4", 0x4B);
		Plain("Numpad5", 0x4C);
		Plain("Numpad6", 0x4D);
		Plain("NumpadAdd", 0x4E);
		Plain("Numpad1", 0x4F);
		Plain("Numpad2", 0x50);
		Plain("Numpad3", 0x51);
		Plain("Numpad0", 0x52);
		Plain("NumpadDecimal", 0x53);

		// The key between the left shift and Z that only ISO keyboards have.
		Plain("IntlBackslash", 0x56);
		Plain("F11", 0x57);
		Plain("F12", 0x58);

		// Japanese and Korean keys, which sit in the gaps of the same block.
		Plain("KanaMode", 0x70);
		Plain("Lang2", 0x71);
		Plain("Lang1", 0x72);
		Plain("IntlRo", 0x73);
		Plain("Convert", 0x79);
		Plain("NonConvert", 0x7B);
		Plain("IntlYen", 0x7D);

		// Everything the AT keyboard prefixes with E0: the duplicates of keys that already exist on the keypad.
		Extended("NumpadEnter", 0x1C);
		Extended("ControlRight", 0x1D);
		Extended("NumpadDivide", 0x35);
		Extended("PrintScreen", 0x37);
		Extended("AltRight", 0x38);

		// Pause is the one key a scancode plus an extended flag cannot say: on the wire it carries a second flag
		// and two codes. E0 45 is what it degrades to, and nothing else uses it.
		Extended("Pause", 0x45);
		Extended("Home", 0x47);
		Extended("ArrowUp", 0x48);
		Extended("PageUp", 0x49);
		Extended("ArrowLeft", 0x4B);
		Extended("ArrowRight", 0x4D);
		Extended("End", 0x4F);
		Extended("ArrowDown", 0x50);
		Extended("PageDown", 0x51);
		Extended("Insert", 0x52);
		Extended("Delete", 0x53);
		Extended("MetaLeft", 0x5B);
		Extended("MetaRight", 0x5C);
		Extended("ContextMenu", 0x5D);

		// Chrome reports the Windows keys as OSLeft and OSRight on some platforms.
		map.Add("OSLeft", map["MetaLeft"]);
		map.Add("OSRight", map["MetaRight"]);

		return map.ToFrozenDictionary(StringComparer.Ordinal);
	}
}
