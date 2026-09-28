namespace Mokaterm.Abstractions.Settings;

/// <summary>What a start does with the sessions that were open when the app last ran.</summary>
public enum SessionRestoreMode
{
	/// <summary>Start with no tabs.</summary>
	Off,

	/// <summary>Offer them on the welcome screen.</summary>
	Ask,

	/// <summary>Open them as soon as the shell is up.</summary>
	Always,
}
