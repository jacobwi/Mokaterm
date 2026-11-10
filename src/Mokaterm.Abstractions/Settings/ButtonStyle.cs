namespace Mokaterm.Abstractions.Settings;

/// <summary>How buttons are drawn. Stored by name; new styles go last.</summary>
public enum ButtonStyle
{
	/// <summary>The theme's own buttons.</summary>
	Standard,

	/// <summary>Lifted with a shadow.</summary>
	Raised,

	/// <summary>Tinted, with no border.</summary>
	Soft,

	/// <summary>Fully rounded.</summary>
	Pill,

	/// <summary>A glow in the button's colour.</summary>
	Glow,

	/// <summary>A solid shadow set off to one side.</summary>
	Offset,

	/// <summary>A gradient fill.</summary>
	Gradient,

	/// <summary>Text only until pointed at.</summary>
	Ghost,
}
