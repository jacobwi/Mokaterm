using System.Text;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Telnet.Protocol;

namespace Mokaterm.Modules.Telnet.Sessions;

/// <summary>Everything one <see cref="TelnetTerminalChannel"/> needs, resolved from the connection and the settings.</summary>
internal sealed record TelnetChannelOptions
{
	/// <summary>Answers to TERMINAL-TYPE SEND, most wanted first.</summary>
	public required IReadOnlyList<string> TerminalTypes { get; init; }

	/// <summary>The character set on the wire. UTF-8 travels unchanged; anything else is transcoded both ways.</summary>
	public required Encoding Encoding { get; init; }

	public TelnetLineEnding LineEnding { get; init; }

	public TelnetEchoMode Echo { get; init; }

	public TerminalSize Size { get; init; } = TerminalSize.Default;

	/// <summary>Idle time before an IAC NOP is sent. Zero sends none.</summary>
	public TimeSpan KeepAlive { get; init; }

	/// <summary>How long the channel keeps watching the output for a login prompt.</summary>
	public TimeSpan AutoLoginTimeout { get; init; } = TimeSpan.FromSeconds(30);

	/// <summary>Owned by the channel, which disposes it with the session.</summary>
	public TelnetAutoLogin? AutoLogin { get; init; }

	public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
