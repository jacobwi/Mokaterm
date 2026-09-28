using System.Buffers;

namespace Mokaterm.Abstractions.Terminal;

/// <summary>
/// Writes what the user typed towards the remote side: the carriage return the terminal sends for Enter becomes the
/// line ending the connection uses, and a byte the protocol reserves is doubled so data can never look like a command.
/// </summary>
/// <remarks>
/// A pasted CR LF is one Enter, and its line feed may arrive in the next write, so one encoder belongs to one
/// connection and keeps its state for as long as that connection lives.
/// </remarks>
public sealed class TerminalOutputEncoder
{
	private const byte CarriageReturn = 0x0D;

	private const byte LineFeed = 0x0A;

	private readonly byte[] _lineEnding;
	private readonly byte? _escape;
	private bool _skipLineFeed;

	/// <param name="lineEnding">What Enter sends: CR LF, CR NUL, a lone CR or a lone LF.</param>
	/// <param name="escape">The byte the protocol reserves, doubled on its way out. Null where every byte is data.</param>
	public TerminalOutputEncoder(ReadOnlySpan<byte> lineEnding, byte? escape = null)
	{
		_lineEnding = lineEnding.ToArray();
		_escape = escape;
	}

	public void Write(ReadOnlySpan<byte> input, IBufferWriter<byte> output)
	{
		ArgumentNullException.ThrowIfNull(output);
		while (!input.IsEmpty)
		{
			// The line feed behind a carriage return is already covered by the sequence that was written for it.
			if (_skipLineFeed)
			{
				_skipLineFeed = false;
				if (input[0] == LineFeed)
				{
					input = input[1..];
					continue;
				}
			}

			int index = _escape is { } escape ? input.IndexOfAny(CarriageReturn, escape) : input.IndexOf(CarriageReturn);
			if (index < 0)
			{
				output.Write(input);
				return;
			}

			output.Write(input[..index]);
			if (input[index] == CarriageReturn)
			{
				output.Write(_lineEnding);
				_skipLineFeed = true;
			}
			else
			{
				Span<byte> escaped = output.GetSpan(2);
				escaped[0] = input[index];
				escaped[1] = input[index];
				output.Advance(2);
			}

			input = input[(index + 1)..];
		}
	}
}
