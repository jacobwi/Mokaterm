namespace Mokaterm.Abstractions.Import;

/// <summary>
/// Drives the import: lists the sources, reads a preview and marks entries that already exist, then creates the
/// folders, hosts and logins through <see cref="Connections.IConnectionRepository"/>.
/// </summary>
public interface IConnectionImporter
{
	IReadOnlyList<ImportSourceInfo> Sources { get; }

	/// <summary>
	/// Reads <paramref name="sourceId"/> and flags every entry that matches a saved login on address, user and
	/// protocol. Entries whose protocol this build does not have are moved to the skipped list.
	/// </summary>
	ValueTask<ImportPreview> PreviewAsync(string sourceId, ImportReadRequest request, CancellationToken cancellationToken = default);

	ValueTask<ImportResult> ImportAsync(ImportRequest request, CancellationToken cancellationToken = default);
}
