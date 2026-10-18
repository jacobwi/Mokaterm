namespace Mokaterm.UI.Common.Platform;

/// <summary>How a clear through <see cref="ISystemClipboard"/> ended.</summary>
public enum SystemClipboardClear
{
	/// <summary>The clipboard still held our copy and is empty now.</summary>
	Cleared,

	/// <summary>Something wrote the clipboard after our copy, so it was left as it is.</summary>
	Changed,

	/// <summary>Another program held the clipboard open the whole time, and the copy may still be there.</summary>
	Busy,

	/// <summary>The system refused the clear, and the copy may still be there.</summary>
	Failed,
}
