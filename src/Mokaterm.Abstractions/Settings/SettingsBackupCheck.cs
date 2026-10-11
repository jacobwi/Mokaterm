namespace Mokaterm.Abstractions.Settings;

/// <summary>What a settings file holds, read before anything is replaced, or why it cannot be imported.</summary>
public sealed record SettingsBackupCheck
{
	/// <summary>Why the file cannot be imported, for the user. Null when it can.</summary>
	public string? Error { get; init; }

	public DateTimeOffset? ExportedAt { get; init; }

	public int Sections { get; init; }

	public bool CanImport => Error is null;
}
