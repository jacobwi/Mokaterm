namespace Mokaterm.Abstractions.Settings;

/// <summary>
/// One key combination: the primary modifier (Ctrl, Cmd on macOS) plus an optional Shift and a browser key code such as
/// <c>KeyB</c> or <c>Comma</c>. An empty code means the command has no shortcut.
/// </summary>
public sealed record ShortcutGesture(string Code, bool Shift)
{
	/// <summary>No shortcut at all.</summary>
	public static ShortcutGesture None { get; } = new("", false);

	public bool IsSet => !string.IsNullOrEmpty(Code);
}
