using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.UI.Terminal.Playback;

/// <summary>
/// Reads a session log for the player. The file is untrusted input: it may have been cut short, edited by hand or made
/// somewhere else entirely, so a line that is not an event the player understands is counted and left out rather than
/// failing the file, and a file past <see cref="MaxFileBytes"/> is refused before any of it is kept.
/// </summary>
public static class TerminalRecordingReader
{
	/// <summary>
	/// The largest file the player opens. All of it goes into the page, and a seek backwards replays it from the start
	/// there, so this is about what a page can hold and redraw, not about the disk.
	/// </summary>
	public const long MaxFileBytes = 16 * 1024 * 1024;

	/// <summary>The longest pause played as it happened. asciinema calls this the idle time limit.</summary>
	public static readonly TimeSpan IdleLimit = TimeSpan.FromSeconds(2);

	/// <summary>Events kept. A recording of a few hundred thousand tiny events stops here and says its end is missing.</summary>
	internal const int MaxEvents = 200_000;

	/// <summary>The longest text one event carries; longer output is split into several events at the same time.</summary>
	internal const int MaxEventLength = 64 * 1024;

	/// <summary>The widest grid a recording may ask for. Every cell of it costs the page memory.</summary>
	internal const int MaxColumns = 500;

	/// <inheritdoc cref="MaxColumns"/>
	internal const int MaxRows = 200;

	/// <summary>Lines that are not events, before a file that has given no event at all is called something else.</summary>
	internal const int MaxSkippedLines = 10_000;

	/// <summary>Output events closer together than this become one, which the eye cannot tell apart.</summary>
	internal static readonly TimeSpan MergeWindow = TimeSpan.FromMilliseconds(10);

	private static readonly TerminalSize CastSize = new(80, 24);

	// A header or an event is a flat array or an object with one level inside; anything deeper is not either.
	private static readonly JsonDocumentOptions JsonOptions = new() { MaxDepth = 8 };

	private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

	/// <summary>The message for a file the player will not open because of its size.</summary>
	public static string TooLarge(long length) => string.Create(
		CultureInfo.CurrentCulture,
		$"This file is {DisplayFormat.Bytes(length)}, and the player opens files up to {DisplayFormat.Bytes(MaxFileBytes)}. Save a copy and play it with asciinema, or open it in an editor.");

	/// <summary>Reads at most <see cref="MaxFileBytes"/> from <paramref name="stream"/> and parses it off the caller's thread.</summary>
	/// <param name="fileName">Decides the format: <c>.cast</c> is an asciicast, anything else a plain or raw log.</param>
	public static async Task<TerminalRecordingResult> ReadAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(stream);
		if (stream.CanSeek && stream.Length > MaxFileBytes)
		{
			return TerminalRecordingResult.Failed(TooLarge(stream.Length));
		}

		// A session can still be writing the file, so it may have grown past the cap since it was listed.
		using MemoryStream content = new();
		byte[] chunk = ArrayPool<byte>.Shared.Rent(81920);
		try
		{
			int read;
			while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
			{
				if (content.Length + read > MaxFileBytes)
				{
					return TerminalRecordingResult.Failed(TooLarge(content.Length + read));
				}

				content.Write(chunk, 0, read);
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(chunk);
		}

		byte[] buffer = content.GetBuffer();
		int length = (int)content.Length;
		return await Task.Run(() => Parse(buffer.AsSpan(0, length), fileName), cancellationToken);
	}

	/// <inheritdoc cref="ReadAsync"/>
	public static TerminalRecordingResult Parse(ReadOnlySpan<byte> content, string fileName)
	{
		ArgumentNullException.ThrowIfNull(fileName);
		if (content.StartsWith("\ufeff"u8))
		{
			content = content[3..];
		}

		return fileName.EndsWith(Asciicast.Extension, StringComparison.OrdinalIgnoreCase)
			? new CastParser().Parse(content)
			: ParseLog(content);
	}

	/// <summary>
	/// A plain or raw log, all at time zero. A file without a single escape is plain text, whose bare line feeds are
	/// line ends; in raw output a bare line feed only moves the cursor down, so that is left alone.
	/// </summary>
	private static TerminalRecordingResult ParseLog(ReadOnlySpan<byte> content)
	{
		string text = Utf8.GetString(content);
		if (!text.Contains('\u001b', StringComparison.Ordinal))
		{
			text = text.ReplaceLineEndings("\r\n");
		}

		List<RecordingEvent> events = [];
		AddPieces(events, TimeSpan.Zero, text);
		TerminalSize size = TerminalSize.Default;
		return TerminalRecordingResult.Read(new TerminalRecording(size.Columns, size.Rows, isTimed: false, events, skippedLines: 0, isTruncated: false));
	}

	/// <summary>Adds <paramref name="text"/> in events of at most <see cref="MaxEventLength"/>, never splitting a surrogate pair.</summary>
	private static void AddPieces(List<RecordingEvent> events, TimeSpan time, string text)
	{
		for (int start = 0; start < text.Length;)
		{
			int length = Math.Min(MaxEventLength, text.Length - start);
			if (start + length < text.Length && char.IsHighSurrogate(text[start + length - 1]))
			{
				length--;
			}

			events.Add(RecordingEvent.Text(time, text.Substring(start, length)));
			start += length;
		}
	}

	/// <summary>One pass over an asciicast, version 2 (times since the start) or 3 (times since the event before).</summary>
	private sealed class CastParser
	{
		private readonly List<RecordingEvent> _events = [];
		private readonly StringBuilder _pending = new();
		private TimeSpan _pendingTime;
		private TimeSpan _clock;
		private double _previous;
		private int _version = Asciicast.Version;
		private TimeSpan _idleLimit = IdleLimit;
		private int _columns = CastSize.Columns;
		private int _rows = CastSize.Rows;
		private int _skipped;
		private bool _truncated;

		public TerminalRecordingResult Parse(ReadOnlySpan<byte> content)
		{
			bool first = true;
			while (!content.IsEmpty)
			{
				int end = content.IndexOf((byte)'\n');
				ReadOnlySpan<byte> line = (end < 0 ? content : content[..end]).TrimEnd("\r"u8).Trim(" \t"u8);
				content = end < 0 ? [] : content[(end + 1)..];
				if (line.IsEmpty)
				{
					continue;
				}

				if (first)
				{
					first = false;
					if (line[0] == (byte)'{')
					{
						if (ReadHeader(line) is { } error)
						{
							return TerminalRecordingResult.Failed(error);
						}

						continue;
					}

					// No header: a file cut at the front still plays at the classic size, but one that does not even start
					// with an event is something else.
					if (line[0] != (byte)'[')
					{
						return TerminalRecordingResult.Failed("This file does not start like an asciicast recording.");
					}
				}

				if (!ReadEvent(line))
				{
					_skipped++;
					if (_skipped >= MaxSkippedLines && _events.Count == 0 && _pending.Length == 0)
					{
						return TerminalRecordingResult.Failed("Nothing in this file could be played.");
					}
				}

				if (_events.Count >= MaxEvents)
				{
					_truncated = true;
					break;
				}
			}

			Flush();
			return TerminalRecordingResult.Read(new TerminalRecording(_columns, _rows, isTimed: true, _events, _skipped, _truncated));
		}

		/// <summary>Reads the header line. Returns why the file cannot be played, or null.</summary>
		private string? ReadHeader(ReadOnlySpan<byte> line)
		{
			try
			{
				using JsonDocument document = JsonDocument.Parse(line.ToArray(), JsonOptions);
				JsonElement header = document.RootElement;
				if (header.ValueKind != JsonValueKind.Object
					|| !header.TryGetProperty("version", out JsonElement version)
					|| version.ValueKind != JsonValueKind.Number
					|| !version.TryGetInt32(out _version))
				{
					return "This file does not start like an asciicast recording.";
				}

				if (_version is not (Asciicast.Version or Asciicast.IntervalVersion))
				{
					return string.Create(CultureInfo.CurrentCulture, $"This is an asciicast version {_version} recording, and the player reads versions 2 and 3.");
				}

				// Version 2 gives the grid as width and height, version 3 as cols and rows under term.
				bool interval = _version == Asciicast.IntervalVersion;
				JsonElement grid = !interval ? header : header.TryGetProperty("term", out JsonElement term) ? term : default;
				_columns = Dimension(grid, interval ? "cols" : "width", CastSize.Columns, MaxColumns);
				_rows = Dimension(grid, interval ? "rows" : "height", CastSize.Rows, MaxRows);

				if (header.TryGetProperty("idle_time_limit", out JsonElement idle)
					&& idle.ValueKind == JsonValueKind.Number
					&& idle.TryGetDouble(out double seconds)
					&& double.IsFinite(seconds)
					&& seconds > 0
					&& seconds < IdleLimit.TotalSeconds)
				{
					_idleLimit = TimeSpan.FromSeconds(seconds);
				}

				return null;
			}
			catch (Exception ex) when (ex is JsonException or InvalidOperationException)
			{
				return "This file does not start like an asciicast recording.";
			}
		}

		private static int Dimension(JsonElement grid, string name, int fallback, int max) =>
			grid.ValueKind == JsonValueKind.Object
			&& grid.TryGetProperty(name, out JsonElement value)
			&& value.ValueKind == JsonValueKind.Number
			&& value.TryGetInt32(out int number)
			&& number > 0
				? Math.Min(number, max)
				: fallback;

		/// <summary>Reads one event line. False for anything that is not one, which the caller counts.</summary>
		private bool ReadEvent(ReadOnlySpan<byte> line)
		{
			// Every event is an array, so anything else is skipped without paying for an exception.
			if (line[0] != (byte)'[' || line[^1] != (byte)']')
			{
				return false;
			}

			try
			{
				using JsonDocument document = JsonDocument.Parse(line.ToArray(), JsonOptions);
				JsonElement root = document.RootElement;
				if (root.GetArrayLength() < 3
					|| !root[0].TryGetDouble(out double time)
					|| !double.IsFinite(time)
					|| root[1].ValueKind != JsonValueKind.String)
				{
					return false;
				}

				// Every event moves the clock, the ones the screen does not show included: in version 3 the next event's
				// time counts from this one.
				TimeSpan at = Advance(time);
				string? code = root[1].GetString();
				if (code is not (Asciicast.OutputCode or Asciicast.ResizeCode))
				{
					// Input, markers and the exit status are part of the format but nothing the screen shows.
					return true;
				}

				if (root[2].ValueKind != JsonValueKind.String)
				{
					return false;
				}

				string data = root[2].GetString() ?? "";
				if (code == Asciicast.OutputCode)
				{
					AddOutput(at, data);
					return true;
				}

				if (!Asciicast.TryParseSize(data, out int columns, out int rows))
				{
					return false;
				}

				Flush();
				_events.Add(RecordingEvent.Resize(at, Math.Min(columns, MaxColumns), Math.Min(rows, MaxRows)));
				return true;
			}
			catch (Exception ex) when (ex is JsonException or InvalidOperationException)
			{
				// Not JSON, or a string JSON allows and .NET will not hold (a lone surrogate).
				return false;
			}
		}

		/// <summary>
		/// The event's time on the player's clock. A time that goes backwards counts as no time at all, which keeps the
		/// events in order whatever the file says, and a pause longer than the idle limit is cut to it.
		/// </summary>
		private TimeSpan Advance(double time)
		{
			double raw = _version == Asciicast.IntervalVersion ? _previous + Math.Max(0, time) : time;
			if (!(raw >= _previous) || !double.IsFinite(raw))
			{
				raw = _previous;
			}

			double gap = Math.Min(raw - _previous, _idleLimit.TotalSeconds);
			_previous = raw;
			_clock += TimeSpan.FromSeconds(gap);
			return _clock;
		}

		private void AddOutput(TimeSpan at, string data)
		{
			if (data.Length == 0)
			{
				return;
			}

			if (_pending.Length > 0 && (at - _pendingTime > MergeWindow || _pending.Length + data.Length > MaxEventLength))
			{
				Flush();
			}

			if (_pending.Length == 0)
			{
				_pendingTime = at;
			}

			_pending.Append(data);
		}

		private void Flush()
		{
			if (_pending.Length > 0)
			{
				AddPieces(_events, _pendingTime, _pending.ToString());
				_pending.Clear();
			}
		}
	}
}
