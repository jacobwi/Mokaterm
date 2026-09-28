namespace Mokaterm.Modules.Serial.Protocol;

/// <summary>Everything needed to open one port, resolved from the connection and the settings.</summary>
internal sealed record SerialOpenRequest
{
	public required string PortName { get; init; }

	public required SerialLineSettings Line { get; init; }

	/// <summary>Whether DTR is asserted as the port opens. Off keeps a board that reboots on DTR from rebooting.</summary>
	public bool Dtr { get; init; }

	/// <summary>Whether RTS is asserted as the port opens. Ignored when RTS/CTS flow control owns the pin.</summary>
	public bool Rts { get; init; }

	/// <summary>How long one read waits before it gives up. <see cref="TimeSpan.Zero"/> waits forever.</summary>
	public TimeSpan ReadTimeout { get; init; }

	/// <summary>How long a write waits for the device to take the bytes. Never zero: a blocked write is the point.</summary>
	public TimeSpan WriteTimeout { get; init; } = TimeSpan.FromSeconds(2);
}
