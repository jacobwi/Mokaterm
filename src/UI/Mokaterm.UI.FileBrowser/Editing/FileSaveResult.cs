namespace Mokaterm.UI.FileBrowser.Editing;

/// <summary>
/// What a save from <see cref="Dialogs.FileEditDialog"/> answers. Public only because the dialog is a public component
/// and its parameters carry it.
/// </summary>
public sealed class FileSaveResult
{
	private FileSaveResult(FileSaveOutcome outcome, string? message, string? serverText, IReadOnlyList<string>? serverFacts)
	{
		Outcome = outcome;
		Message = message;
		ServerText = serverText;
		ServerFacts = serverFacts;
	}

	public static FileSaveResult Saved { get; } = new(FileSaveOutcome.Saved, null, null, null);

	public FileSaveOutcome Outcome { get; }

	/// <summary>
	/// For <see cref="FileSaveOutcome.Failed"/> the sentence to show. For a conflict without <see cref="ServerText"/>,
	/// the sentence that says why the server's copy cannot be shown.
	/// </summary>
	public string? Message { get; }

	/// <summary>For a conflict, the server's current contents with line endings normalized the way the editor's are.</summary>
	public string? ServerText { get; }

	/// <summary>For a conflict with <see cref="ServerText"/>, the size, encoding and line ending of that copy, one chip each.</summary>
	public IReadOnlyList<string>? ServerFacts { get; }

	public static FileSaveResult Failed(string message) => new(FileSaveOutcome.Failed, message, null, null);

	public static FileSaveResult Conflict(string serverText, IReadOnlyList<string> serverFacts) =>
		new(FileSaveOutcome.Conflict, null, serverText, serverFacts);

	public static FileSaveResult UnreadableConflict(string reason) => new(FileSaveOutcome.Conflict, reason, null, null);
}
