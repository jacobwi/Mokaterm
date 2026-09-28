using System.Text;

namespace Mokaterm.Modules.Serial.Sessions;

/// <summary>Everything one <see cref="SerialTerminalChannel"/> needs, resolved from the connection and the settings.</summary>
internal sealed record SerialChannelOptions
{
	/// <summary>The character set on the line. UTF-8 travels unchanged; anything else is transcoded both ways.</summary>
	public required Encoding Encoding { get; init; }

	public SerialLineEnding LineEnding { get; init; }

	/// <summary>Show what is typed, for a device that echoes nothing.</summary>
	public bool LocalEcho { get; init; }

	/// <summary>How often the pins are read while the line is quiet. Zero turns the check off.</summary>
	public TimeSpan DeviceCheck { get; init; } = TimeSpan.FromSeconds(2);

	/// <summary>How long a break holds the line low.</summary>
	public TimeSpan Break { get; init; } = TimeSpan.FromMilliseconds(250);

	public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
