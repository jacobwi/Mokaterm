namespace Mokaterm.Abstractions.Connections;

/// <summary>A folder in the connection tree. Folders nest through <see cref="ParentId"/>.</summary>
public sealed record ConnectionFolder
{
	public required Guid Id { get; init; }

	public required string Name { get; init; }

	public Guid? ParentId { get; init; }

	public int Order { get; init; }
}
