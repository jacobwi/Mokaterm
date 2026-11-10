namespace Mokaterm.Abstractions.Settings;

/// <summary>How the rows of a list that opens from a field are drawn. Stored by name; new styles go last.</summary>
public enum ListStyle
{
	/// <summary>The theme's own rows.</summary>
	Standard,

	/// <summary>A check beside the chosen row.</summary>
	Check,

	/// <summary>The chosen row filled with the accent.</summary>
	Solid,

	/// <summary>The list joined to its field.</summary>
	Attached,

	/// <summary>A radio mark on each row.</summary>
	Radio,

	/// <summary>Shorter rows.</summary>
	Compact,

	/// <summary>Monospaced rows.</summary>
	Terminal,
}
