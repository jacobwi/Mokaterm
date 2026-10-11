namespace Mokaterm.UI.QuickConnect;

/// <summary>What a row under the quick connect box stands for.</summary>
public enum QuickConnectRowKind
{
	/// <summary>A saved login, which opens through the same connect action as the connections panel.</summary>
	SavedLogin,

	/// <summary>A target quick connect opened before, from the vault's history.</summary>
	Recent,

	/// <summary>An address the protocol found on this computer, such as a serial port or a WSL distribution.</summary>
	Detected,

	/// <summary>Opens the login editor with the typed target, and connects once it is saved.</summary>
	ConnectAndSave,

	/// <summary>Forgets every recent target.</summary>
	ClearHistory,
}
