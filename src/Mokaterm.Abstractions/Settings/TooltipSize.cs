namespace Mokaterm.Abstractions.Settings;

/// <summary>How much room a tooltip's text takes. Stored by name; new sizes go last.</summary>
public enum TooltipSize
{
	/// <summary>The size tooltips have always had.</summary>
	Standard,

	/// <summary>Small text with little padding.</summary>
	Compact,

	/// <summary>Larger text with more padding.</summary>
	Roomy,
}
