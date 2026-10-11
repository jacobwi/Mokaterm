namespace Mokaterm.UI.QuickConnect;

/// <summary>The vault document behind <see cref="QuickConnectHistory"/>: the targets quick connect opened, newest first.</summary>
internal sealed class QuickConnectHistoryDocument
{
	/// <summary>Encrypted with the vault key: every entry names a machine and usually a user on it.</summary>
	public const string DocumentName = "quick-connect-history";

	public List<QuickConnectRecent> Entries { get; set; } = [];
}
