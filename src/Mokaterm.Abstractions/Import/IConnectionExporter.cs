namespace Mokaterm.Abstractions.Import;

/// <summary>
/// Writes the saved hosts and logins to a file the import wizard reads back. Passwords, keys and passphrases are never
/// part of it: an export is for moving a setup to another machine, not for carrying secrets around.
/// </summary>
public interface IConnectionExporter
{
	ValueTask<ConnectionExport> ExportAsync(CancellationToken cancellationToken = default);
}
