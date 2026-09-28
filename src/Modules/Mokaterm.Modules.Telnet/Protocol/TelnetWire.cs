using System.Buffers;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Modules.Telnet.Protocol;

/// <summary>Writes the protocol bytes: commands, negotiations and subnegotiations, with IAC doubled everywhere.</summary>
internal static class TelnetWire
{
	/// <summary>Largest value NAWS can carry. RFC 1073 sends the size as two unsigned 16 bit numbers.</summary>
	public const int MaxWindowDimension = ushort.MaxValue;

	public static void WriteCommand(IBufferWriter<byte> output, byte command)
	{
		ArgumentNullException.ThrowIfNull(output);
		Span<byte> span = output.GetSpan(2);
		span[0] = TelnetCommand.Iac;
		span[1] = command;
		output.Advance(2);
	}

	public static void WriteNegotiation(IBufferWriter<byte> output, byte command, byte option)
	{
		ArgumentNullException.ThrowIfNull(output);
		Span<byte> span = output.GetSpan(3);
		span[0] = TelnetCommand.Iac;
		span[1] = command;
		span[2] = option;
		output.Advance(3);
	}

	/// <summary>IAC SB option, the payload with IAC doubled, then IAC SE.</summary>
	public static void WriteSubnegotiation(IBufferWriter<byte> output, byte option, ReadOnlySpan<byte> payload)
	{
		ArgumentNullException.ThrowIfNull(output);
		WriteNegotiation(output, TelnetCommand.Sb, option);
		WriteEscaped(output, payload);
		WriteCommand(output, TelnetCommand.Se);
	}

	/// <summary>Copies data with every 255 doubled, which is what keeps payload bytes out of the command path.</summary>
	public static void WriteEscaped(IBufferWriter<byte> output, ReadOnlySpan<byte> data)
	{
		ArgumentNullException.ThrowIfNull(output);
		while (!data.IsEmpty)
		{
			int index = data.IndexOf(TelnetCommand.Iac);
			if (index < 0)
			{
				output.Write(data);
				return;
			}

			output.Write(data[..index]);
			Span<byte> escaped = output.GetSpan(2);
			escaped[0] = TelnetCommand.Iac;
			escaped[1] = TelnetCommand.Iac;
			output.Advance(2);
			data = data[(index + 1)..];
		}
	}

	/// <summary>The window size as RFC 1073 wants it: width then height, each a big endian 16 bit number.</summary>
	public static void WriteWindowSize(IBufferWriter<byte> output, TerminalSize size)
	{
		int columns = Math.Clamp(size.Columns, 0, MaxWindowDimension);
		int rows = Math.Clamp(size.Rows, 0, MaxWindowDimension);
		Span<byte> payload =
		[
			(byte)(columns >> 8),
			(byte)columns,
			(byte)(rows >> 8),
			(byte)rows,
		];

		WriteSubnegotiation(output, TelnetOption.NegotiateAboutWindowSize, payload);
	}
}
