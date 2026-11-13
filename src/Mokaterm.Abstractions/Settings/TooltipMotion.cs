namespace Mokaterm.Abstractions.Settings;

/// <summary>How a tooltip comes into view. Stored by name; new kinds go last.</summary>
public enum TooltipMotion
{
	/// <summary>It fades in.</summary>
	Fade,

	/// <summary>It fades in while moving a few pixels away from what it explains.</summary>
	Slide,

	/// <summary>It fades in while growing slightly.</summary>
	Grow,

	/// <summary>It appears at once.</summary>
	Instant,
}
