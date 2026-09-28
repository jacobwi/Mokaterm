using System.Diagnostics.CodeAnalysis;

namespace Mokaterm.Abstractions.FileSystem;

/// <summary>A user or group on the server.</summary>
/// <param name="Name">The name listings show and <see cref="IRemoteFileSystem.SetOwnerAsync"/> takes.</param>
/// <param name="Id">The uid or gid.</param>
public sealed record RemoteAccount(string Name, long Id)
{
	/// <summary>
	/// True for a name an owner or group change can carry: not empty, and no whitespace, control characters, ':' or '/'.
	/// chown takes <c>owner:group</c> as one argument, so a colon can never be part of a name.
	/// </summary>
	public static bool IsValidName([NotNullWhen(true)] string? name) =>
		!string.IsNullOrEmpty(name)
		&& !name.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || character is ':' or '/');
}

/// <summary>The users and groups a server knows, in the order its account databases list them.</summary>
public sealed record RemoteAccounts
{
	public static RemoteAccounts Empty { get; } = new();

	public IReadOnlyList<RemoteAccount> Users { get; init; } = [];

	public IReadOnlyList<RemoteAccount> Groups { get; init; } = [];

	public bool IsEmpty => Users.Count == 0 && Groups.Count == 0;
}
