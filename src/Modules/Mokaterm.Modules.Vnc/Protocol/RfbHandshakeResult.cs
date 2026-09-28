namespace Mokaterm.Modules.Vnc.Protocol;

/// <summary>
/// What the handshake leaves behind: the stream the session runs on (TLS wrapped for the encrypted VeNCrypt
/// subtypes), the ServerInit the page side is given later, and the security type that got us here.
/// </summary>
internal sealed record RfbHandshakeResult(Stream Stream, RfbServerInit ServerInit, VncSecurityType Security);
