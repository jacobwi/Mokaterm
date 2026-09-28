using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Modules.Telnet.Protocol;

/// <summary>
/// The character set of a telnet connection. Resolving a name is <see cref="TerminalEncodings"/>, shared with every
/// protocol that carries a terminal; this is only what the <c>telnet.encoding</c> key falls back to.
/// </summary>
internal static class TelnetEncodings
{
	/// <summary>What a connection uses when it names no character set.</summary>
	public const string Default = TerminalEncodings.Default;
}
