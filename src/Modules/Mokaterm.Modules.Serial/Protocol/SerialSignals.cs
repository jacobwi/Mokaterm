namespace Mokaterm.Modules.Serial.Protocol;

/// <summary>
/// The control lines at one moment. <see cref="Dtr"/> and <see cref="Rts"/> are what this end drives; the rest is
/// what the device drives, read from the pins.
/// </summary>
public sealed record SerialSignals
{
	public static SerialSignals Unknown { get; } = new();

	/// <summary>Data Terminal Ready, driven by us. Asserting it resets many boards, an Arduino among them.</summary>
	public bool Dtr { get; init; }

	/// <summary>Request To Send, driven by us unless RTS/CTS flow control owns it.</summary>
	public bool Rts { get; init; }

	/// <summary>Clear To Send, driven by the device. Low with RTS/CTS flow control means writes are held back.</summary>
	public bool Cts { get; init; }

	/// <summary>Data Set Ready, driven by the device.</summary>
	public bool Dsr { get; init; }

	/// <summary>Carrier Detect, driven by the device. Modems use it; a console cable usually leaves it low.</summary>
	public bool CarrierDetect { get; init; }

	/// <summary>True while this end is holding the line in a break.</summary>
	public bool Break { get; init; }
}
