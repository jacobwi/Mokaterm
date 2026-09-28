namespace Mokaterm.Modules.Rdp;

/// <summary>
/// The desktop features a session asks the server to keep. Turning them off is what makes RDP usable over a slow
/// link, so the defaults here are the ones a remote session wants rather than the ones a local desktop has.
/// </summary>
public sealed record RdpPerformanceOptions
{
	public static RdpPerformanceOptions Default { get; } = new();

	/// <summary>The desktop background picture. Off by default: it is the single largest thing a session redraws.</summary>
	public bool Wallpaper { get; init; }

	/// <summary>Visual styles. Off by default, which gives the classic flat window chrome.</summary>
	public bool Themes { get; init; }

	/// <summary>Subpixel text smoothing. On by default because text is most of what a session shows.</summary>
	public bool FontSmoothing { get; init; } = true;

	/// <summary>Drawing a window's content while it is dragged, instead of an outline.</summary>
	public bool FullWindowDrag { get; init; }
}
