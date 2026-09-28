using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Serial.Protocol;

/// <summary>
/// Turns what opening a port throws into a <see cref="ProtocolConnectException"/> the user can act on. The driver's
/// own messages name a Win32 error and nothing else, which is why every case here is rewritten.
/// </summary>
internal static class SerialConnectErrors
{
	public static ProtocolConnectException Describe(Exception exception, string portName, SerialLineSettings line)
	{
		ArgumentNullException.ThrowIfNull(exception);
		ArgumentNullException.ThrowIfNull(line);
		return exception switch
		{
			ProtocolConnectException connect => connect,

			PlatformNotSupportedException platform =>
				new(ConnectFailure.HostUnreachable, SerialPlatform.Unsupported, platform),

			// Another program, or another session in this window, has the port. A port is opened by one process only.
			UnauthorizedAccessException denied => new(
				ConnectFailure.HostUnreachable,
				$"{portName} is in use. A serial port is opened by one program at a time, this app's own sessions included.",
				denied),

			FileNotFoundException missing => new(ConnectFailure.HostUnreachable, NotFound(portName), missing),

			// A name the driver refuses outright, or a baud rate or data bit count it cannot do.
			ArgumentException argument => new(
				ConnectFailure.HostUnreachable,
				$"{portName} cannot be opened: {argument.Message}",
				argument),

			IOException io => new(ConnectFailure.HostUnreachable, FromIoError(io, portName, line), io),

			_ => new(ConnectFailure.Unknown, $"Could not open {portName}: {exception.Message}", exception),
		};
	}

	private static string NotFound(string portName) =>
		$"{portName} was not found. Check the name, and that the adapter is plugged in.";

	/// <summary>
	/// An <see cref="IOException"/> from an open is either a port that is not there or a frame the UART cannot do.
	/// The line is checked first, since that is the case a user can fix from the connection's own options.
	/// </summary>
	private static string FromIoError(IOException io, string portName, SerialLineSettings line) =>
		line.Validate() is { } invalid
			? $"{portName} cannot run at {line.Frame}: {invalid}"
			: $"{portName} could not be opened: {io.Message}";
}
