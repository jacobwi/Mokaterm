namespace Mokaterm.Abstractions.Import;

/// <summary>What a source found, ready to show as a checklist. Never null, never an exception: see <see cref="Availability"/>.</summary>
public sealed record ImportPreview
{
	public required string SourceId { get; init; }

	public ImportAvailability Availability { get; init; }

	/// <summary>Where the entries came from, for display: a file path or a registry key.</summary>
	public string? Location { get; init; }

	/// <summary>Why nothing came back, or a note about what was read. Null when there is nothing to say.</summary>
	public string? Message { get; init; }

	public IReadOnlyList<ImportedEntry> Entries { get; init; } = [];

	public IReadOnlyList<ImportSkip> Skipped { get; init; } = [];

	public static ImportPreview Nothing(string sourceId, string message, string? location = null) => new()
	{
		SourceId = sourceId,
		Availability = ImportAvailability.NotFound,
		Location = location,
		Message = message,
	};

	public static ImportPreview Failed(string sourceId, string message, string? location = null) => new()
	{
		SourceId = sourceId,
		Availability = ImportAvailability.Unreadable,
		Location = location,
		Message = message,
	};
}
