using System.Buffers;
using System.Text;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Ssh.Tools;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// The output of a build or a push as lines, gathered while the command runs: both streams decoded as UTF-8 (a character
/// split between two reads arrives whole), cut at every line break, a carriage return included, and made plain. It hears
/// the command on the reading loops, two at once, and a view takes a <see cref="Snapshot"/> on its own thread.
/// </summary>
/// <remarks>
/// Memory is bounded the way <see cref="LogTail"/> bounds a log: at most <see cref="LogTail.MaxLines"/> lines are kept,
/// the oldest dropped first and counted, and a line longer than <see cref="MaxLineCharacters"/> is cut into pieces of
/// that length. Nothing is queued for the view: what it has not looked at by then is simply gone from the top.
/// </remarks>
internal sealed class DockerOutputLines
{
	/// <summary>The longest piece of a line kept as one line.</summary>
	public const int MaxLineCharacters = 1024;

	private readonly Lock _lock = new();
	private readonly Queue<LogLine> _lines = new();
	private readonly Reader _output = new(CommandStream.Output);
	private readonly Reader _error = new(CommandStream.Error);
	private readonly bool _errorIsProgress;
	private long _dropped;
	private int _version;

	/// <param name="errorIsProgress">
	/// stderr carries progress rather than errors, as BuildKit's does, so only the lines that read as errors are marked
	/// as such; otherwise every stderr line is.
	/// </param>
	public DockerOutputLines(bool errorIsProgress) => _errorIsProgress = errorIsProgress;

	/// <summary>Bumped by every change, so a view can tell whether to take a new snapshot.</summary>
	public int Version
	{
		get
		{
			lock (_lock)
			{
				return _version;
			}
		}
	}

	/// <summary>How many of the oldest lines were dropped to keep the rest.</summary>
	public long Dropped
	{
		get
		{
			lock (_lock)
			{
				return _dropped;
			}
		}
	}

	/// <summary>The handler a call is given (<see cref="DockerCall.OnOutput"/>).</summary>
	public void Write(CommandStream stream, ReadOnlySpan<byte> bytes)
	{
		if (bytes.IsEmpty)
		{
			return;
		}

		lock (_lock)
		{
			Reader reader = stream == CommandStream.Error ? _error : _output;
			int count = reader.Decoder.GetCharCount(bytes, flush: false);
			char[] chars = ArrayPool<char>.Shared.Rent(Math.Max(count, 1));
			try
			{
				int decoded = reader.Decoder.GetChars(bytes, chars, flush: false);
				Take(reader, chars.AsSpan(0, decoded));
			}
			finally
			{
				ArrayPool<char>.Shared.Return(chars);
			}

			_version++;
		}
	}

	/// <summary>Ends both streams: whatever is left of a last line without a line break becomes a line.</summary>
	public void Complete()
	{
		Span<char> rest = stackalloc char[4];
		lock (_lock)
		{
			foreach (Reader reader in (Reader[])[_output, _error])
			{
				int decoded = reader.Decoder.GetChars([], rest, flush: true);
				Take(reader, rest[..decoded]);
				if (reader.Line.Length > 0)
				{
					End(reader);
				}
			}

			_version++;
		}
	}

	/// <summary>Every line kept, oldest first, with the line still being written at the end of each stream.</summary>
	public IReadOnlyList<LogLine> Snapshot()
	{
		lock (_lock)
		{
			List<LogLine> lines = [.. _lines];
			foreach (Reader reader in (Reader[])[_output, _error])
			{
				if (reader.Line.Length > 0)
				{
					lines.Add(Made(reader.Stream, reader.Line.ToString()));
				}
			}

			return lines;
		}
	}

	/// <summary>The last line that reads as an error, for the run's own message; null when there is none.</summary>
	public string? LastError()
	{
		lock (_lock)
		{
			return _lines.LastOrDefault(line => line.IsError && line.Text.Trim().Length > 0)?.Text.Trim();
		}
	}

	/// <summary>
	/// True for a line a build or a push writes when it fails: BuildKit's <c>ERROR:</c> and <c>#5 ERROR:</c>, the
	/// daemon's <c>Error response from daemon</c>, and the registry's <c>denied:</c> and <c>unauthorized:</c>.
	/// </summary>
	internal static bool LooksLikeError(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		ReadOnlySpan<char> line = text.AsSpan().TrimStart();
		if (line.StartsWith("#", StringComparison.Ordinal))
		{
			int space = line.IndexOf(' ');
			if (space > 1 && int.TryParse(line[1..space], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
			{
				line = line[(space + 1)..];
			}
		}

		return line.StartsWith("ERROR", StringComparison.Ordinal)
			|| line.StartsWith("error:", StringComparison.OrdinalIgnoreCase)
			|| line.StartsWith("Error response from daemon", StringComparison.Ordinal)
			|| line.StartsWith("failed to solve", StringComparison.OrdinalIgnoreCase)
			|| line.StartsWith("denied:", StringComparison.OrdinalIgnoreCase)
			|| line.StartsWith("unauthorized:", StringComparison.OrdinalIgnoreCase);
	}

	private void Take(Reader reader, ReadOnlySpan<char> chars)
	{
		foreach (char character in chars)
		{
			if (character == '\n' && reader.AfterReturn)
			{
				reader.AfterReturn = false;
				continue;
			}

			reader.AfterReturn = character == '\r';
			if (character is '\n' or '\r')
			{
				End(reader);
				continue;
			}

			reader.Line.Append(character);
			if (reader.Line.Length >= MaxLineCharacters)
			{
				End(reader);
			}
		}
	}

	private void End(Reader reader)
	{
		_lines.Enqueue(Made(reader.Stream, reader.Line.ToString()));
		reader.Line.Clear();
		while (_lines.Count > LogTail.MaxLines)
		{
			_ = _lines.Dequeue();
			_dropped++;
		}
	}

	private LogLine Made(CommandStream stream, string raw)
	{
		string text = TerminalTextFilter.Plain(raw);
		bool error = stream == CommandStream.Error && (!_errorIsProgress || LooksLikeError(text));
		return new LogLine("", text, error);
	}

	private sealed class Reader(CommandStream stream)
	{
		public CommandStream Stream { get; } = stream;

		public Decoder Decoder { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetDecoder();

		public StringBuilder Line { get; } = new();

		public bool AfterReturn { get; set; }
	}
}
