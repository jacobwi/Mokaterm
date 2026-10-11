namespace Mokaterm.UI.FileBrowser.Editing;

/// <summary>What a save from the file editor came to.</summary>
public enum FileSaveOutcome
{
	/// <summary>The file was written.</summary>
	Saved,

	/// <summary>Nothing was written, and <see cref="FileSaveResult.Message"/> says why.</summary>
	Failed,

	/// <summary>
	/// Nothing was written because the file changed on the server after the editor last read it. The result carries the
	/// server's text when it can be shown, and the reason when it cannot.
	/// </summary>
	Conflict,
}
