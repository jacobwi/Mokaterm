using Mokaterm.Modules.Serial.Protocol;

namespace Mokaterm.Modules.Serial;

/// <summary>
/// The live port behind one session, as the serial panel in the session toolbar sees it. Reached through
/// <c>IProtocolSession.GetFeature&lt;ISerialPortFeature&gt;()</c>.
/// </summary>
public interface ISerialPortFeature
{
	/// <summary>Raised after the pins, the write counters or the break state change. Handlers may run on any thread.</summary>
	event Action? Changed;

	SerialPortStatus Status { get; }

	/// <summary>Drives DTR. On a board that reboots when DTR rises, this is the reset button.</summary>
	Task SetDtrAsync(bool on, CancellationToken cancellationToken = default);

	/// <summary>Drives RTS. Refused while RTS/CTS flow control owns the pin.</summary>
	Task SetRtsAsync(bool on, CancellationToken cancellationToken = default);

	/// <summary>
	/// Holds the line low for the length in settings, then releases it. A Linux console takes this as SysRq and a
	/// bootloader as "stop and listen to me".
	/// </summary>
	Task SendBreakAsync(CancellationToken cancellationToken = default);

	/// <summary>Reads the pins now instead of waiting for the next check.</summary>
	Task RefreshAsync(CancellationToken cancellationToken = default);
}
