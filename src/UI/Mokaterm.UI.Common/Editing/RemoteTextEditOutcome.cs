namespace Mokaterm.UI.Common.Editing;

/// <summary>How an edit through <see cref="IRemoteTextEditor"/> ended.</summary>
public enum RemoteTextEditOutcome
{
	/// <summary>The editor was closed without writing, or the edit was cancelled before it opened.</summary>
	Closed,

	/// <summary>A save went through before the editor closed.</summary>
	Saved,

	/// <summary>The file was read and is not something the editor can hand back unchanged. The user was told why.</summary>
	Refused,

	/// <summary>The file could not be created or read. Nothing was shown; the result says why.</summary>
	Failed,
}
