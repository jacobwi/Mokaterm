namespace Mokaterm.Abstractions.Import;

/// <summary>What the import wizard shows for one source before anything is read.</summary>
public sealed record ImportSourceInfo
{
	public required string Id { get; init; }

	/// <summary>Name of the client, for example <c>PuTTY</c>.</summary>
	public required string DisplayName { get; init; }

	/// <summary>One line about what is read, for example "Sessions saved in the registry".</summary>
	public string? Description { get; init; }

	public ImportSourceKind Kind { get; init; }

	/// <summary>Extensions the file picker should suggest, for example <c>.reg</c>. Empty for installed sources.</summary>
	public IReadOnlyList<string> FileExtensions { get; init; } = [];

	/// <summary>True when the source can also read a file the user picks, on top of its own location.</summary>
	public bool AcceptsFile { get; init; }
}
