namespace Mokaterm.Modules.Serial.Protocol;

/// <summary>
/// One open port: the byte stream and the control lines beside it. The terminal channel works through this
/// interface, so the tests can drive it over a stream of their own instead of a real adapter.
/// </summary>
internal interface ISerialLink : IAsyncDisposable
{
	string PortName { get; }

	SerialLineSettings Line { get; }

	/// <summary>The data stream. Reads and writes carry the device's bytes and nothing else.</summary>
	Stream Stream { get; }

	/// <summary>False when RTS/CTS flow control owns the RTS pin, which makes toggling it an error.</summary>
	bool CanSetRts { get; }

	/// <summary>False once the driver has refused to report CTS, DSR and CD, which some virtual ports do.</summary>
	bool ReportsPins { get; }

	/// <summary>
	/// Reads the pins. Throws when the port is gone, which is how an unplugged adapter is noticed while the line
	/// is quiet.
	/// </summary>
	SerialSignals ReadSignals();

	void SetDtr(bool on);

	/// <summary>Drives RTS. Only legal while <see cref="CanSetRts"/> is true.</summary>
	void SetRts(bool on);

	/// <summary>Holds the line low (a break) or releases it.</summary>
	void SetBreak(bool on);
}
