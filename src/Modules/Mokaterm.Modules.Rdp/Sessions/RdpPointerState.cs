namespace Mokaterm.Modules.Rdp.Sessions;

/// <summary>The pointer the server last asked for: a picture it drew, hidden, or the default one.</summary>
/// <param name="Image">The picture, or null for the default pointer or none at all.</param>
/// <param name="Hidden">True when the server hid the pointer. Only read when there is no picture.</param>
internal sealed record RdpPointerState(RdpPointerPixels? Image, bool Hidden)
{
	public static RdpPointerState Default { get; } = new(null, Hidden: false);

	public static RdpPointerState HiddenPointer { get; } = new(null, Hidden: true);
}
