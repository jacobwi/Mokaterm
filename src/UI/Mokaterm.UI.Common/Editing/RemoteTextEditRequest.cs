using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.Common.Editing;

/// <summary>
/// One file for <see cref="IRemoteTextEditor"/>. A class and not a record, so nothing prints the path it names.
/// </summary>
public sealed class RemoteTextEditRequest
{
	/// <summary>
	/// The file system the file is read from and written back to: the login's, or a root view of it to edit as root.
	/// It belongs to whoever opened it; the editor never disposes it.
	/// </summary>
	public required IRemoteFileSystem FileSystem { get; init; }

	/// <summary>The absolute path of the file.</summary>
	public required string Path { get; init; }

	/// <summary>What the editor's title and its notices call the file, or null for the last part of <see cref="Path"/>.</summary>
	public string? DisplayName { get; init; }

	/// <summary>
	/// Creates the file empty before opening it, for a file the user is making: something already at the path fails the
	/// edit with <see cref="RemoteFileErrorKind.AlreadyExists"/> rather than being opened in its place. The file stays
	/// when the editor is closed without saving, as an empty file of that name.
	/// </summary>
	public bool CreateNew { get; init; }
}
