namespace Mokaterm.Modules.Rdp;

/// <summary>
/// One thing the user did, in the compact shape <c>rdp.js</c> sends. Pointer events fill <see cref="A"/> and
/// <see cref="B"/>; key events fill <see cref="Code"/>.
/// </summary>
public sealed record RdpInputEvent
{
	public RdpInputKind Kind { get; init; }

	public int A { get; init; }

	public int B { get; init; }

	/// <summary>A <c>KeyboardEvent.code</c> such as <c>KeyA</c>, <c>Digit1</c> or <c>ControlLeft</c>.</summary>
	public string? Code { get; init; }

	public static RdpInputEvent MouseMove(int x, int y) => new() { Kind = RdpInputKind.MouseMove, A = x, B = y };

	public static RdpInputEvent MouseDown(int button) => new() { Kind = RdpInputKind.MouseDown, A = button };

	public static RdpInputEvent MouseUp(int button) => new() { Kind = RdpInputKind.MouseUp, A = button };

	/// <param name="units">Positive is the direction the page reports, which is down or right.</param>
	public static RdpInputEvent Wheel(bool vertical, int units) => new() { Kind = RdpInputKind.Wheel, A = vertical ? 1 : 0, B = units };

	public static RdpInputEvent KeyDown(string code) => new() { Kind = RdpInputKind.KeyDown, Code = code };

	public static RdpInputEvent KeyUp(string code) => new() { Kind = RdpInputKind.KeyUp, Code = code };

	public static RdpInputEvent ReleaseKeys() => new() { Kind = RdpInputKind.ReleaseKeys };
}
