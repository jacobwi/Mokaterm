namespace Mokaterm.Abstractions.Terminal;

public enum TerminalCursorStyle
{
	Block,
	Underline,
	Bar,
}

public enum TerminalBellStyle
{
	None,
	Visual,
	Sound,
}

public enum TerminalRightClickAction
{
	ContextMenu,
	Paste,
}

/// <summary>How the user, the machine and the folder of a <c>user@host:path$</c> prompt are colored.</summary>
public enum TerminalPromptColors
{
	/// <summary>The prompt looks exactly as the server sent it.</summary>
	Off,

	/// <summary>Only a prompt the server sends in one plain color is painted, the way root's arrives on most server images.</summary>
	WhenPlain,

	/// <summary>Painted even when the server colors the prompt itself, so the user and the machine always differ.</summary>
	Always,
}

/// <summary>Per-connection terminal appearance. Null properties inherit the global terminal settings.</summary>
public sealed record TerminalProfileOverrides
{
	public string? ThemeId { get; init; }

	public string? FontFamily { get; init; }

	public double? FontSize { get; init; }

	public bool IsEmpty => ThemeId is null && FontFamily is null && FontSize is null;
}
