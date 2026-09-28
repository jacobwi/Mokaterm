using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Modules.Serial.Protocol;

/// <summary>
/// The character set of a serial connection. Resolving a name is <see cref="TerminalEncodings"/>, shared with every
/// protocol that carries a terminal; this is only what the <c>serial.encoding</c> key falls back to.
/// </summary>
internal static class SerialEncodings
{
	/// <summary>What a connection uses when it names no character set.</summary>
	public const string Default = TerminalEncodings.Default;
}
