namespace Mokaterm.Abstractions.Settings;

/// <summary>What a session log file holds.</summary>
public enum SessionLogFormat
{
	/// <summary>Escape sequences and control bytes are dropped, so the file reads as plain text.</summary>
	PlainText,

	/// <summary>Every byte the server sent, colors and cursor moves included. Replay it with <c>cat</c>.</summary>
	Raw,

	/// <summary>
	/// What the server sent with the time each piece arrived, as an asciicast v2 recording (<c>.cast</c>). The settings
	/// page plays it back, and so does asciinema.
	/// </summary>
	Asciicast,
}
