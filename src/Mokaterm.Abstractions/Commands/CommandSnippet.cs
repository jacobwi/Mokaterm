namespace Mokaterm.Abstractions.Commands;

/// <summary>Which sessions a saved command is offered in.</summary>
public enum CommandSnippetScope
{
	/// <summary>Every session, whichever machine it runs on.</summary>
	Global,

	/// <summary>Sessions to the same machine, over any login.</summary>
	Host,

	/// <summary>Sessions opened from one saved login.</summary>
	Connection,
}

/// <summary>
/// A command kept for later: saved from a terminal row or written by hand. Commands can hold secrets, so they live in
/// the vault and never in the settings file.
/// </summary>
public sealed record CommandSnippet
{
	public required Guid Id { get; init; }

	/// <summary>Shown in the picker. Derived from the command when the user does not type one.</summary>
	public required string Name { get; init; }

	/// <summary>The command line, without a trailing line break.</summary>
	public required string Command { get; init; }

	public IReadOnlyList<string> Tags { get; init; } = [];

	public CommandSnippetScope Scope { get; init; }

	/// <summary>
	/// The machine the command was saved on. Set for <see cref="CommandSnippetScope.Host"/> and
	/// <see cref="CommandSnippetScope.Connection"/>, and kept for connection snippets so the list still says where
	/// they came from.
	/// </summary>
	public string? Host { get; init; }

	/// <summary>The saved login, for <see cref="CommandSnippetScope.Connection"/> only.</summary>
	public Guid? ConnectionId { get; init; }

	/// <summary>
	/// True when picking the command runs it, false when it is only typed at the prompt. Off for anything saved with
	/// the quick button: running by itself is always a deliberate choice.
	/// </summary>
	public bool RunImmediately { get; init; }

	public DateTimeOffset CreatedAt { get; init; }

	public DateTimeOffset? LastUsedAt { get; init; }

	public int UseCount { get; init; }
}

/// <summary>The session a command list is built for: its machine and, when the login is saved, its id.</summary>
public sealed record CommandSnippetTarget
{
	/// <summary>The host address of the session. Null matches global commands only.</summary>
	public string? Host { get; init; }

	/// <summary>The saved login behind the session, or null for quick-connect sessions.</summary>
	public Guid? ConnectionId { get; init; }
}
