namespace Mokaterm.UI.QuickConnect;

/// <summary>How well a row matches the typed text, best first.</summary>
public enum QuickConnectRank
{
	/// <summary>A name, an address or <c>user@host</c> is exactly what was typed.</summary>
	Exact,

	/// <summary>One of them starts with what was typed.</summary>
	Prefix,

	/// <summary>A word inside one of them, or a tag, starts with what was typed.</summary>
	WordPrefix,

	/// <summary>What was typed appears somewhere inside one of them.</summary>
	Contains,

	None,
}
