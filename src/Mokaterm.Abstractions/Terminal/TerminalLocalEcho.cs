using System.Buffers;

namespace Mokaterm.Abstractions.Terminal;

/// <summary>
/// Turns what was typed into what the terminal should show while the remote side echoes nothing. Only the keys whose
/// effect the terminal cannot guess are translated; everything else is shown as it was typed.
/// </summary>
public static class TerminalLocalEcho
{
	private const byte Backspace = 0x08;

	private const byte CarriageReturn = 0x0D;

	private const byte LineFeed = 0x0A;

	private const byte Delete = 0x7F;

	public static byte[] Build(ReadOnlySpan<byte> input)
	{
		ArrayBufferWriter<byte> writer = new(input.Length + 8);
		for (int i = 0; i < input.Length; i++)
		{
			switch (input[i])
			{
				// Enter has to become a full new line, or the next line would be drawn over this one.
				case CarriageReturn:
					writer.Write("\r\n"u8);

					// A pasted CR LF is one new line, not two.
					if (i + 1 < input.Length && input[i + 1] == LineFeed)
					{
						i++;
					}

					break;

				// Both erase keys only move the cursor left; the space is what takes the character off the screen.
				case Backspace:
				case Delete:
					writer.Write("\b \b"u8);
					break;

				default:
					Span<byte> span = writer.GetSpan(1);
					span[0] = input[i];
					writer.Advance(1);
					break;
			}
		}

		return writer.WrittenSpan.ToArray();
	}
}
