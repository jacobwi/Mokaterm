namespace Mokaterm.Abstractions.Settings;

/// <summary>How tall buttons, toggle groups and segmented controls are beside text fields. Stored by name; new ones go last.</summary>
public enum ControlHeights
{
	/// <summary>Each kind of control keeps its own height.</summary>
	Standard,

	/// <summary>Buttons, toggle groups and segmented controls as tall as a text field of the same size.</summary>
	Matched,
}
