namespace Mokaterm.Modules.Rdp;

/// <summary>A pointer the server drew, as a PNG the page can hand straight to the CSS <c>cursor</c> property.</summary>
/// <param name="Png">The pointer picture, alpha included.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="HotspotX">The pixel inside the picture that points at things.</param>
/// <param name="HotspotY">The vertical half of the hotspot.</param>
public sealed record RdpPointerImage(byte[] Png, int Width, int Height, int HotspotX, int HotspotY);
