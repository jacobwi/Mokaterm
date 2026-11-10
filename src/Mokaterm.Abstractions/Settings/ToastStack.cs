namespace Mokaterm.Abstractions.Settings;

/// <summary>How several toasts at once are laid out. Stored by name; new ones go last.</summary>
public enum ToastStack
{
	/// <summary>One under another.</summary>
	List,

	/// <summary>The older ones tucked behind the newest, fanned out on hover or focus.</summary>
	Pile,
}
