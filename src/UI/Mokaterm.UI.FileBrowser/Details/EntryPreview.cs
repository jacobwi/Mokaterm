namespace Mokaterm.UI.FileBrowser.Details;

/// <summary>
/// A file's preview for the details pane. A class rather than a record, because the text can be an env file's
/// passwords and a record's generated ToString would print it.
/// </summary>
internal sealed class EntryPreview
{
	private EntryPreview(EntryPreviewState state, string? text = null, string? imageAddress = null, bool isCut = false, string? problem = null)
	{
		State = state;
		Text = text;
		ImageAddress = imageAddress;
		IsCut = isCut;
		Problem = problem;
	}

	public static EntryPreview None { get; } = new(EntryPreviewState.None);

	public static EntryPreview Empty { get; } = new(EntryPreviewState.Empty);

	public static EntryPreview TooLarge { get; } = new(EntryPreviewState.TooLarge);

	public static EntryPreview Binary { get; } = new(EntryPreviewState.Binary);

	public static EntryPreview NotUtf8 { get; } = new(EntryPreviewState.NotUtf8);

	public EntryPreviewState State { get; }

	/// <summary>The start of the file with escapes and control characters taken out, lines ending in a line feed.</summary>
	public string? Text { get; }

	/// <summary>A <c>data:</c> address of the image, for an <c>img</c>, which never runs what an SVG holds.</summary>
	public string? ImageAddress { get; }

	/// <summary>True when the file goes on past <see cref="Text"/>.</summary>
	public bool IsCut { get; }

	public string? Problem { get; }

	public static EntryPreview OfText(string text, bool isCut) => new(EntryPreviewState.Text, text: text, isCut: isCut);

	public static EntryPreview OfImage(string address) => new(EntryPreviewState.Image, imageAddress: address);

	public static EntryPreview OfFailure(string problem) => new(EntryPreviewState.Failed, problem: problem);
}
