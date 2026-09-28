namespace Mokaterm.Abstractions.Import;

/// <summary>
/// A file the user picked. The bytes travel with it because the web host hands the browser's upload over without a
/// path; <see cref="Path"/> is set on desktop and lets an OpenSSH config resolve its <c>Include</c> lines.
/// </summary>
public sealed record ImportFile
{
	public required string Name { get; init; }

	/// <summary>Full path when the host has one, otherwise null.</summary>
	public string? Path { get; init; }

	public required byte[] Content { get; init; }
}
