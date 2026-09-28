namespace Mokaterm.Sessions.Terminal;

/// <summary>
/// Strips escape sequences and control bytes out of terminal output so a log file reads as plain text. The state
/// carries across calls, because a sequence may straddle any number of reads.
/// <para>
/// Only ASCII escapes and C0 controls are touched. A byte over 0x7f is passed through, because in a UTF-8 stream it is
/// text: the C1 range 0x80..0x9f that a terminal would read as a control is also every continuation byte of a
/// multi-byte character, and dropping those would cut holes in words.
/// </para>
/// </summary>
internal sealed class TerminalTextFilter
{
	private const byte Bell = 0x07;
	private const byte Tab = 0x09;
	private const byte LineFeed = 0x0a;
	private const byte CarriageReturn = 0x0d;
	private const byte Escape = 0x1b;
	private const byte Space = 0x20;
	private const byte Backslash = 0x5c;
	private const byte Delete = 0x7f;

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
	/// </summary>
	public int Filter(ReadOnlySpan<byte> input, Span<byte> output)
	{
		int written = 0;
		foreach (byte value in input)
		{
			switch (_mode)
			{
				case Mode.Text:
					if (value == Escape)
					{
						_mode = Mode.AfterEscape;
					}
					else if (value is Tab or LineFeed or CarriageReturn || (value >= Space && value != Delete))
					{
						output[written++] = value;
					}

					break;

				case Mode.AfterEscape:
					_mode = value switch
					{
						Escape => Mode.AfterEscape,
						(byte)'[' => Mode.Csi,
						(byte)']' or (byte)'P' or (byte)'X' or (byte)'^' or (byte)'_' => Mode.StringSequence,
						(byte)'(' or (byte)')' or (byte)'*' or (byte)'+' or (byte)'-' or (byte)'.' or (byte)'/'
							or (byte)'#' or (byte)'%' or Space => Mode.Designator,

						// A two-byte escape such as ESC c or ESC 7: this byte was the whole of it.
						_ => Mode.Text,
					};
					break;

				case Mode.Csi:
					// Parameter and intermediate bytes run 0x20..0x3f; anything in 0x40..0x7e ends the sequence. A control
					// byte in the middle is dropped with it, which is what a malformed sequence deserves in a text file.
					if (value == Escape)
					{
						_mode = Mode.AfterEscape;
					}
					else if (value >= 0x40 && value <= 0x7e)
					{
						_mode = Mode.Text;
					}

					break;

				case Mode.StringSequence:
					_mode = value switch
					{
						Bell => Mode.Text,
						Escape => Mode.StringEscape,
						_ => Mode.StringSequence,
					};
					break;

				case Mode.StringEscape:
					_mode = value switch
					{
						Backslash => Mode.Text,
						Escape => Mode.StringEscape,
						_ => Mode.StringSequence,
					};
					break;

				case Mode.Designator:
				default:
					_mode = Mode.Text;
					break;
			}
		}

		return written;
	}
}
