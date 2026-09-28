namespace Mokaterm.Abstractions.Commands;

/// <summary>
/// Saved commands, encrypted with the vault key. Every member throws <see cref="Security.VaultLockedException"/>
/// while the vault is locked. <see cref="SaveAsync"/> inserts or updates and normalizes what it stores.
/// </summary>
public interface ICommandSnippetStore
{
	/// <summary>Raised after any change, including changes made from another window or browser tab.</summary>
	event Action? Changed;

	/// <summary>Every saved command, newest first.</summary>
	ValueTask<IReadOnlyList<CommandSnippet>> ListAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// The commands offered in a session, most useful first: this login, then this machine, then global, and within
	/// each group the ones used most and most recently.
	/// </summary>
	ValueTask<IReadOnlyList<CommandSnippet>> QueryAsync(CommandSnippetTarget target, CancellationToken cancellationToken = default);

	/// <summary>Stores <paramref name="snippet"/> and returns it as stored, with the timestamps filled in.</summary>
	Task<CommandSnippet> SaveAsync(CommandSnippet snippet, CancellationToken cancellationToken = default);

	Task DeleteAsync(Guid snippetId, CancellationToken cancellationToken = default);

	/// <summary>Records that a command was picked: bumps its use count and last used time.</summary>
	Task MarkUsedAsync(Guid snippetId, CancellationToken cancellationToken = default);
}
