namespace Mokaterm.Abstractions.Settings;

/// <summary>
/// Every setting to one file and back, for moving a setup to another machine or keeping a copy. The file is the plain
/// settings document, which never holds a secret, without the values marked <see cref="MachineSpecificAttribute"/>; an
/// import keeps this machine's own instead. Connections, credentials and saved commands live in the vault and are not
/// part of it.
/// </summary>
public interface ISettingsBackup
{
	Task<SettingsBackupFile> ExportAsync(CancellationToken cancellationToken = default);

	/// <summary>Reads a file without applying it, so the confirmation can say what it holds or why it is refused.</summary>
	SettingsBackupCheck Check(string fileName, byte[] content);

	/// <summary>
	/// Replaces every setting with the file's: a section the file does not hold goes back to its defaults, and every value
	/// passes the same checks as one read from settings.json. Throws <see cref="InvalidDataException"/> with the reason
	/// <see cref="Check"/> gives when the file cannot be imported.
	/// </summary>
	Task ImportAsync(string fileName, byte[] content, CancellationToken cancellationToken = default);
}
