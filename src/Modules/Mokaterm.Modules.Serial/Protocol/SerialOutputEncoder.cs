using System.Buffers;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Modules.Serial.Protocol;

/// <summary>
/// Writes what the user typed down the line. A serial line has no framing and no escaping, so the only change is
/// the carriage return the terminal sends for Enter, which becomes the ending the connection is set to.
/// </summary>
internal sealed class SerialOutputEncoder(SerialLineEnding lineEnding)
{
	private const byte CarriageReturn = 0x0D;

	private const byte LineFeed = 0x0A;

	private readonly TerminalOutputEncoder _encoder = new(Bytes(lineEnding));

	public void Write(ReadOnlySpan<byte> input, IBufferWriter<byte> output) => _encoder.Write(input, output);

	private static byte[] Bytes(SerialLineEnding lineEnding) => lineEnding switch
	{
		SerialLineEnding.CrLf => [CarriageReturn, LineFeed],
		SerialLineEnding.Lf => [LineFeed],
		_ => [CarriageReturn],
	};
}
