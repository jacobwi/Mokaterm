using System.Text;

namespace Mokaterm.Abstractions.Terminal;

/// <summary>
/// Strips escape sequences and control characters out of terminal output so it reads as plain text: a session log, a
/// command run's output, a container's log, what a system view script printed. Over bytes the state carries across
/// calls, because a sequence may straddle any number of reads; <see cref="Plain"/> cleans one decoded string whole.
/// <para>
/// Over bytes only ASCII escapes and C0 controls are touched. A byte over 0x7f is passed through, because in a UTF-8
/// stream it is text: the C1 range 0x80..0x9f that a terminal would read as a control is also every continuation byte
/// of a multi-byte character, and dropping those would cut holes in words. Decoded text has no such overlap, so
/// <see cref="Plain"/> drops the C1 controls as well.
/// </para>
/// </summary>
public sealed class TerminalTextFilter
{
	private const int Bell = 0x07;
	private const int Tab = 0x09;
	private const int LineFeed = 0x0a;
	private const int CarriageReturn = 0x0d;
	private const int Escape = 0x1b;
	private const int Space = 0x20;
	private const int Backslash = 0x5c;
	private const int Delete = 0x7f;

	private Mode _mode;

	private enum Mode
	{
		Text,

		/// <summary>An ESC arrived; the next byte says what kind of sequence it is.</summary>
		AfterEscape,

		/// <summary>Inside <c>ESC [</c>, up to a final byte in 0x40..0x7e.</summary>
		Csi,

		/// <summary>Inside OSC, DCS, SOS, PM or APC, up to a BEL or a string terminator.</summary>
		StringSequence,

		/// <summary>An ESC inside a string sequence: a backslash after it ends the string.</summary>
		StringEscape,

		/// <summary>A character set designator such as <c>ESC ( B</c>, which takes exactly one more byte.</summary>
		Designator,
	}

	/// <summary>
	/// Copies the printable part of <paramref name="input"/> into <paramref name="output"/> and returns how much was
	/// written. Filtering only ever drops bytes, so <paramref name="output"/> needs room for <paramref name="input"/>.
	/// Carriage returns stay, so a log file keeps the line endings the server sent.
	/// </summary>
	public int Filter(ReadOnlySpan<byte> input, Span<byte> output)
	{
		int written = 0;
		foreach (byte value in input)
		{
			if (Accept(value))
			{
				output[written++] = value;
			}
		}

		return written;
	}

	/// <summary>
	/// <paramref name="text"/> without escape sequences and without control characters other than tabs and line
	/// breaks, with every line break a line feed: what a static box can show of whatever a command printed. A lone
	/// carriage return becomes a line break too, so the steps of a progress line read one under the other instead of
	/// running together.
	/// </summary>
	public static string Plain(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		if (!NeedsCleaning(text))
		{
			return text;
		}

		TerminalTextFilter filter = new();
		StringBuilder plain = new(text.Length);
		foreach (char character in text)
		{
			if (filter.Accept(character) && character is not (>= '\u0080' and <= '\u009f'))
			{
				plain.Append(character);
			}
		}

		return plain.ToString().ReplaceLineEndings("\n");
	}

	private static bool NeedsCleaning(string text)
	{
		foreach (char character in text)
		{
			if (char.IsControl(character) && character is not ('\t' or '\n'))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>Moves past one byte or character and says whether it is text to keep.</summary>
	private bool Accept(int value)
	{
		switch (_mode)
		{
			case Mode.Text:
				if (value == Escape)
				{
					_mode = Mode.AfterEscape;
					return false;
				}

				return value is Tab or LineFeed or CarriageReturn || (value >= Space && value != Delete);

			case Mode.AfterEscape:
				_mode = value switch
				{
					Escape => Mode.AfterEscape,
					'[' => Mode.Csi,
					']' or 'P' or 'X' or '^' or '_' => Mode.StringSequence,
					'(' or ')' or '*' or '+' or '-' or '.' or '/' or '#' or '%' or Space => Mode.Designator,

					// A two-byte escape such as ESC c or ESC 7: this byte was the whole of it.
					_ => Mode.Text,
				};
				return false;

			case Mode.Csi:
				// Parameter and intermediate bytes run 0x20..0x3f; anything in 0x40..0x7e ends the sequence. A control
				// byte in the middle is dropped with it, which is what a malformed sequence deserves in plain text.
				if (value == Escape)
				{
					_mode = Mode.AfterEscape;
				}
				else if (value is >= 0x40 and <= 0x7e)
				{
					_mode = Mode.Text;
				}

				return false;

			case Mode.StringSequence:
				_mode = value switch
				{
					Bell => Mode.Text,
					Escape => Mode.StringEscape,
					_ => Mode.StringSequence,
				};
				return false;

			case Mode.StringEscape:
				_mode = value switch
				{
					Backslash => Mode.Text,
					Escape => Mode.StringEscape,
					_ => Mode.StringSequence,
				};
				return false;

			case Mode.Designator:
			default:
				_mode = Mode.Text;
				return false;
		}
	}
}
