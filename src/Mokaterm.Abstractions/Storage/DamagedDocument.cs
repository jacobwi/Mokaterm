namespace Mokaterm.Abstractions.Storage;

/// <summary>
/// A stored document that could not be decrypted or parsed. It was moved aside, so reading it now finds nothing and the
/// next write starts it over.
/// </summary>
public sealed record DamagedDocument
{
	/// <summary>The document name, such as <c>connections</c>.</summary>
	public required string Name { get; init; }

	/// <summary>
	/// Where the damaged copy is: the file it was moved to, or the file itself when it could not be moved.
	/// </summary>
	public required string Path { get; init; }

	/// <summary>
	/// False when the file could not be moved aside. It then stays in place, and the next write of the document replaces
	/// it, damaged copy and all.
	/// </summary>
	public bool MovedAside { get; init; }
}
