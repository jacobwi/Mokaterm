namespace Mokaterm.Modules.Vnc;

/// <summary>What the server told us about itself while connecting.</summary>
/// <param name="DesktopName">The name from ServerInit, for example <c>root's X desktop (host:1)</c>.</param>
/// <param name="Width">Screen width in pixels at the time of the handshake.</param>
/// <param name="Height">Screen height in pixels at the time of the handshake.</param>
/// <param name="IsEncrypted">True when the session runs inside TLS.</param>
/// <param name="Security">How the session was secured, for example <c>VNC password over TLS (certificate)</c>.</param>
public sealed record VncConnectionInfo(string DesktopName, int Width, int Height, bool IsEncrypted, string Security);
