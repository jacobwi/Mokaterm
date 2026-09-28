using Mokaterm.Abstractions.Security;

namespace Mokaterm.UI.Common.Formatting;

/// <summary>
/// One wording for anything that failed against the vault, shared by the shell's editors and the settings pages. A
/// locked vault is reported as itself and never wrapped: its exception message is plumbing, and unlocking is the answer.
/// </summary>
public static class VaultFailure
{
	/// <summary>
	/// What to show when the vault is locked. <paramref name="action"/> is what the user was after, as an infinitive
	/// without its "to": "save this host", "delete saved commands".
	/// </summary>
	public static string Locked(string action) => $"Unlock the vault to {action}.";

	/// <summary>
	/// What to show when <paramref name="failure"/> stopped <paramref name="action"/>: the locked message for a locked
	/// vault, otherwise what the failure itself said.
	/// </summary>
	public static string Describe(Exception failure, string action) => failure is VaultLockedException
		? Locked(action)
		: $"Could not {action}: {failure.Message}";
}
