namespace Mokaterm.UI.FileBrowser.Browsing;

/// <summary>How much room the browser has on screen. Each step shows more columns and toolbar buttons.</summary>
internal enum BrowserWidth
{
	/// <summary>A side panel: name and size columns, with the less used actions in a menu.</summary>
	Narrow,

	/// <summary>Adds the modified column and every toolbar button.</summary>
	Medium,

	/// <summary>Adds the permissions and owner columns.</summary>
	Wide,
}
