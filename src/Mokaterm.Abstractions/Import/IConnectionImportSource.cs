namespace Mokaterm.Abstractions.Import;

/// <summary>
/// Reads saved hosts out of another client. Implementations only read: they never write to a config file or the
/// registry, and they never import stored passwords.
/// </summary>
public interface IConnectionImportSource
{
	ImportSourceInfo Info { get; }

	/// <summary>
	/// Reads the entries. A source that finds nothing returns <see cref="ImportAvailability.NotFound"/> with a
	/// message rather than throwing.
	/// </summary>
	ValueTask<ImportPreview> ReadAsync(ImportReadRequest request, CancellationToken cancellationToken = default);
}
