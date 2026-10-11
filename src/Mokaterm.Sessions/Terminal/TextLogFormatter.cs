using System.Buffers;
using System.Globalization;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Sessions.Terminal;

/// <summary>
/// The plain text and raw formats: the server's bytes, through <see cref="TerminalTextFilter"/> for plain text or as
/// they came for raw, behind two header lines. Neither keeps time or size.
/// </summary>
internal sealed class TextLogFormatter : SessionLogFormatter
{
	/// <summary>The header's second line: what the file holds, and what it deliberately does not.</summary>
	private const string HeaderNotice =
		"[mokaterm] what the server printed, opening with the scrollback that was already on screen. Nothing typed is recorded.\r\n";

	private readonly TerminalTextFilter? _filter;

	/// <param name="filter">Drops escapes and control bytes for plain text; null keeps every byte.</param>
	public TextLogFormatter(TerminalTextFilter? filter) => _filter = filter;

	public override string Extension => ".log";

	public override byte[] Header(string endpoint, DateTimeOffset startedAt, TerminalSize size) =>
		NoticeEncoding.GetBytes(string.Create(
			CultureInfo.InvariantCulture,
			$"[mokaterm] session log: {endpoint}, started {startedAt:yyyy-MM-dd HH:mm:ss zzz}\r\n{HeaderNotice}"));

	public override bool Append(ReadOnlySpan<byte> data, TimeSpan elapsed, TerminalSize size, long room, ArrayBufferWriter<byte> output)
	{
		Span<byte> target = output.GetSpan(data.Length);
		int length;
		if (_filter is { } filter)
		{
			// Filtering only ever drops bytes, so the span sized for the input is enough.
			length = filter.Filter(data, target);
		}
		else
		{
			data.CopyTo(target);
			length = data.Length;
		}

		// Cut at the cap rather than drop the chunk: a chunk can be larger than the whole cap (the replay buffer a
		// recording starts with is), and a log that stops mid-line beats one that never started.
		if (length > room)
		{
			output.Advance((int)Math.Max(0, room));
			return false;
		}

		output.Advance(length);
		return true;
	}

	public override byte[] LimitNotice(long kilobytes, TimeSpan elapsed) => NoticeEncoding.GetBytes(LimitText(kilobytes));
}
