namespace Mokaterm.UI.Terminal.Playback;

/// <summary>
/// One thing a recording shows: text the server printed, or the terminal taking a new size. <see cref="Time"/> is on
/// the player's clock, with idle gaps already cut down to <see cref="TerminalRecordingReader.IdleLimit"/>.
/// </summary>
public readonly record struct RecordingEvent(TimeSpan Time, string? Output, int Columns, int Rows)
{
	public bool IsResize => Output is null;

	public static RecordingEvent Text(TimeSpan time, string output) => new(time, output, 0, 0);

	public static RecordingEvent Resize(TimeSpan time, int columns, int rows) => new(time, null, columns, rows);
}
