namespace Mokaterm.UI.FileBrowser.Browsing;

/// <summary>What a paste does with names the target folder already has.</summary>
internal enum PasteConflictChoice
{
	/// <summary>Each copy takes a free name of its own: <c>report copy.txt</c>.</summary>
	KeepBoth,

	/// <summary>Each copy replaces what has its name; a folder merges into the folder there.</summary>
	Overwrite,
}
