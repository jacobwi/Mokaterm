namespace Mokaterm.UI.Terminal.Playback;

/// <summary>
/// A session log read for playback: the grid it starts at and its events in time order. A plain or raw log has no times,
/// so every event sits at zero and the player shows it all at once.
/// </summary>
public sealed class TerminalRecording
{
	internal TerminalRecording(int columns, int rows, bool isTimed, IReadOnlyList<RecordingEvent> events, int skippedLines, bool isTruncated)
	{
		Columns = columns;
		Rows = rows;
		IsTimed = isTimed;
		Events = events;
		SkippedLines = skippedLines;
		IsTruncated = isTruncated;
	}

	/// <summary>The grid at the start, before any resize event.</summary>
	public int Columns { get; }

	/// <inheritdoc cref="Columns"/>
	public int Rows { get; }

	/// <summary>True for an asciicast, false for a plain or raw log.</summary>
	public bool IsTimed { get; }

	/// <summary>In time order: an event never comes before the one ahead of it.</summary>
	public IReadOnlyList<RecordingEvent> Events { get; }

	/// <summary>Lines of the file that were not events the player understood, which were left out.</summary>
	public int SkippedLines { get; }

	/// <summary>True when the file held more than the player keeps, so its end is missing.</summary>
	public bool IsTruncated { get; }

	public TimeSpan Duration => Events.Count == 0 ? TimeSpan.Zero : Events[^1].Time;

	/// <summary>How many events the screen shows at <paramref name="position"/>: every one at or before it.</summary>
	public int IndexAt(TimeSpan position)
	{
		int low = 0;
		int high = Events.Count;
		while (low < high)
		{
			int middle = low + ((high - low) / 2);
			if (Events[middle].Time <= position)
			{
				low = middle + 1;
			}
			else
			{
				high = middle;
			}
		}

		return low;
	}
}
