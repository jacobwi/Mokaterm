namespace Mokaterm.Modules.Rdp.Sessions;

/// <summary>A pointer the server drew, copied out of IronRDP as rows of RGBA pixels. It becomes a PNG outside the session's gate.</summary>
/// <param name="Rgba">Width times height times four bytes, top row first, alpha included.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="HotspotX">The pixel inside the picture that points at things.</param>
/// <param name="HotspotY">The vertical half of the hotspot.</param>
internal sealed record RdpPointerPixels(byte[] Rgba, int Width, int Height, int HotspotX, int HotspotY);
