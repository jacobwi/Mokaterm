namespace Mokaterm.UI.Terminal.Playback;

/// <summary>A recording that could be read, or what to tell the user about one that could not.</summary>
public sealed record TerminalRecordingResult
{
	public TerminalRecording? Recording { get; private init; }

	public string? Error { get; private init; }

	public static TerminalRecordingResult Read(TerminalRecording recording) => new() { Recording = recording };

	public static TerminalRecordingResult Failed(string error) => new() { Error = error };
}
