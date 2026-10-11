using System.Buffers;
using System.Text;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Sessions.Terminal;

namespace Mokaterm.Sessions.Commands;

/// <summary>Turns what a command wrote into text for a monospace box: no escape sequences, one kind of line break.</summary>
internal static class CommandOutputDecoder
{
	public static CommandOutputText Decode(CapturedOutput captured)
	{
		ArgumentNullException.ThrowIfNull(captured);
		if (captured.TotalBytes == 0)
		{
			return CommandOutputText.Empty;
		}

		ReadOnlySpan<byte> bytes = captured.Bytes.Span;
		if (captured.IsTruncated)
		{
			// The cap can fall inside a character; the half that made it would only decode as a replacement mark.
			bytes = bytes[..CompleteLength(bytes)];
		}

		byte[] filtered = ArrayPool<byte>.Shared.Rent(Math.Max(bytes.Length, 1));
		try
		{
			// Colours and cursor moves mean nothing in a static box, and a command that forces them is common enough.
			int length = new TerminalTextFilter().Filter(bytes, filtered);
			string text = Encoding.UTF8.GetString(filtered, 0, length).ReplaceLineEndings("\n");
			return new CommandOutputText(text, captured.TotalBytes, captured.IsTruncated);
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(filtered);
		}
	}

	/// <summary>How much of <paramref name="bytes"/> ends on a whole UTF-8 character.</summary>
	internal static int CompleteLength(ReadOnlySpan<byte> bytes)
	{
		int start = bytes.Length - 1;
		int continuations = 0;
		while (start >= 0 && continuations < 3 && (bytes[start] & 0xC0) == 0x80)
		{
			start--;
			continuations++;
		}

		if (start < 0)
		{
			return bytes.Length;
		}

		byte lead = bytes[start];
		int needed = lead switch
		{
			< 0x80 => 1,
			_ when (lead & 0xE0) == 0xC0 => 2,
			_ when (lead & 0xF0) == 0xE0 => 3,
			_ when (lead & 0xF8) == 0xF0 => 4,

			// Not a lead byte at all: the bytes are not UTF-8 there, so there is nothing to keep whole.
			_ => 1,
		};

		return bytes.Length - start < needed ? start : bytes.Length;
	}
}
