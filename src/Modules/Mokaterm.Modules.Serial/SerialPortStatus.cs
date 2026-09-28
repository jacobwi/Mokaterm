using Mokaterm.Modules.Serial.Protocol;

namespace Mokaterm.Modules.Serial;

/// <summary>What the session's serial panel shows: the line it is running on, the pins, and how it treats input.</summary>
public sealed record SerialPortStatus
{
	public required string PortName { get; init; }

	public required SerialLineSettings Line { get; init; }

	public SerialSignals Signals { get; init; } = SerialSignals.Unknown;

	/// <summary>False once the line dropped or the session closed.</summary>
	public bool IsOpen { get; init; }

	/// <summary>False when RTS/CTS flow control drives RTS, which makes the pin the driver's to set.</summary>
	public bool CanSetRts { get; init; }

	/// <summary>False when the driver does not report CTS, DSR and CD at all, which some virtual ports do not.</summary>
	public bool ReportsPins { get; init; } = true;

	public bool LocalEcho { get; init; }

	public SerialLineEnding LineEnding { get; init; }

	public string EncodingName { get; init; } = "";

	/// <summary>
	/// How many writes the device did not take in time. Anything above zero means flow control, or a device that
	/// stopped listening.
	/// </summary>
	public int WriteTimeouts { get; init; }
}
