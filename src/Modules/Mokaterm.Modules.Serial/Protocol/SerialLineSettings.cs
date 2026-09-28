using System.Globalization;

namespace Mokaterm.Modules.Serial.Protocol;

/// <summary>
/// The five values that make up a serial line, plus the rules a port has to satisfy before it can be opened.
/// Checking them here turns a driver's "The parameter is incorrect" into a sentence the user can act on.
/// </summary>
public sealed record SerialLineSettings
{
	/// <summary>Slowest rate offered. Below this no console speaks, and 0 would be refused by the driver.</summary>
	public const int MinBaudRate = 50;

	/// <summary>
	/// Fastest rate offered. Drivers take more, but a USB adapter that cannot do it fails the open with an
	/// unhelpful error, so the field refuses it first.
	/// </summary>
	public const int MaxBaudRate = 4_000_000;

	public const int MinDataBits = 5;

	public const int MaxDataBits = 8;

	/// <summary>The rates the editor offers. Any other rate can still be typed.</summary>
	public static readonly int[] CommonBaudRates =
		[300, 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600];

	public static SerialLineSettings Default { get; } = new();

	/// <summary>Bits per second. 115200 is what most boards and bootloaders ship with.</summary>
	public int BaudRate { get; init; } = 115200;

	public int DataBits { get; init; } = 8;

	public SerialParity Parity { get; init; }

	public SerialStopBits StopBits { get; init; }

	public SerialFlowControl FlowControl { get; init; }

	/// <summary>
	/// False when RTS/CTS flow control drives RTS. Setting the pin by hand is then an error, in the options editor
	/// and on a live port alike.
	/// </summary>
	public bool CanSetRts => FlowControl is not (SerialFlowControl.RtsCts or SerialFlowControl.RtsCtsXOnXOff);

	/// <summary>The line as <c>115200 8N1</c>, the way every data sheet writes it.</summary>
	public string Frame => string.Create(
		CultureInfo.InvariantCulture,
		$"{BaudRate} {DataBits}{ParityLetter}{StopBitsText}");

	/// <summary>The line and its flow control, for a status line.</summary>
	public string Describe() => $"{Frame}, {FlowControlText}";

	/// <summary>A message naming what a port cannot be opened with, or null when the line is usable.</summary>
	public string? Validate()
	{
		if (BaudRate is < MinBaudRate or > MaxBaudRate)
		{
			return $"Use a baud rate from {MinBaudRate} to {MaxBaudRate}.";
		}

		if (DataBits is < MinDataBits or > MaxDataBits)
		{
			return $"Use {MinDataBits} to {MaxDataBits} data bits.";
		}

		// Both combinations are refused by the UART itself, not by us: a frame carries either five data bits with
		// one and a half stop bits, or six to eight with one or two.
		if (StopBits == SerialStopBits.OnePointFive && DataBits != 5)
		{
			return "One and a half stop bits only work with five data bits.";
		}

		return StopBits == SerialStopBits.Two && DataBits == 5
			? "Five data bits cannot be used with two stop bits. Use one and a half."
			: null;
	}

	private char ParityLetter => Parity switch
	{
		SerialParity.Odd => 'O',
		SerialParity.Even => 'E',
		SerialParity.Mark => 'M',
		SerialParity.Space => 'S',
		_ => 'N',
	};

	private string StopBitsText => StopBits switch
	{
		SerialStopBits.OnePointFive => "1.5",
		SerialStopBits.Two => "2",
		_ => "1",
	};

	private string FlowControlText => FlowControl switch
	{
		SerialFlowControl.XOnXOff => "XON/XOFF",
		SerialFlowControl.RtsCts => "RTS/CTS",
		SerialFlowControl.RtsCtsXOnXOff => "RTS/CTS and XON/XOFF",
		_ => "no flow control",
	};
}
