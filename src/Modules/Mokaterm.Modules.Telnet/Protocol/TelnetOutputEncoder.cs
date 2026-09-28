using System.Buffers;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Modules.Telnet.Protocol;

/// <summary>
/// Writes what the user typed as telnet data: the carriage return the terminal sends for Enter becomes the line
/// ending the connection uses, and every 255 is doubled so data can never look like a command.
/// </summary>
/// <remarks>
/// The line ending is the one the connection is set to even after both sides agree on binary transmission, where
/// RFC 856 no longer demands CR NUL. What a device wants for Enter is its own habit, not a consequence of that.
/// </remarks>
internal sealed class TelnetOutputEncoder(TelnetLineEnding lineEnding)
{
	private const byte CarriageReturn = 0x0D;

	private const byte LineFeed = 0x0A;

	private readonly TerminalOutputEncoder _encoder = new(Bytes(lineEnding), TelnetCommand.Iac);

	public void Write(ReadOnlySpan<byte> input, IBufferWriter<byte> output) => _encoder.Write(input, output);

	private static byte[] Bytes(TelnetLineEnding lineEnding) => lineEnding switch
	{
		TelnetLineEnding.CrNul => [CarriageReturn, 0x00],
		TelnetLineEnding.Lf => [LineFeed],
		_ => [CarriageReturn, LineFeed],
	};
}
