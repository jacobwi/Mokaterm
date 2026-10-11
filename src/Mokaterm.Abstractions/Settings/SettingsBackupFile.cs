namespace Mokaterm.Abstractions.Settings;

/// <summary>The file a settings export produced.</summary>
public sealed record SettingsBackupFile
{
	public required string FileName { get; init; }

	public required byte[] Content { get; init; }

	/// <summary>How many sections are in it; a section never changed from its defaults is not.</summary>
	public int Sections { get; init; }
}
