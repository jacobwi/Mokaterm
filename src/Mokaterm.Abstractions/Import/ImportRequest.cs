namespace Mokaterm.Abstractions.Import;

/// <summary>The entries the user ticked and where they should land.</summary>
public sealed record ImportRequest
{
	public required IReadOnlyList<ImportedEntry> Entries { get; init; }

	/// <summary>Existing folder the hosts land in, or null for the top level.</summary>
	public Guid? FolderId { get; init; }

	/// <summary>Name of a folder to create under <see cref="FolderId"/> first. Blank uses <see cref="FolderId"/> as it is.</summary>
	public string? NewFolderName { get; init; }
}
