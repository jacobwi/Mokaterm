namespace Mokaterm.UI.FileBrowser.Compare;

/// <summary>How a file found under the same relative path on both sides compares.</summary>
internal enum FolderCompareStatus
{
	OnlyLocal,

	/// <summary>On the server only. Nothing on the server is ever deleted, so this is for information.</summary>
	OnlyRemote,

	Same,

	LocalNewer,

	RemoteNewer,

	/// <summary>The times agree (or are not compared, or not known) but the sizes do not.</summary>
	SizeDiffers,
}
