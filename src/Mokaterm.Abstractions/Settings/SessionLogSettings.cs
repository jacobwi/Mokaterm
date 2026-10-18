namespace Mokaterm.Abstractions.Settings;

/// <summary>
/// Recording what a session's terminal shows into a file, one file per session. Off by default: the file holds
/// everything the server printed, which can be a secret, a token or a whole directory of host names. Nothing here is
/// itself a secret, which is why it lives in the plain settings document.
/// </summary>
public sealed record SessionLogSettings : ISettingsSection
{
	public static string SectionKey => "sessionlog";

	public const int MinSizeLimitKilobytes = 16;

	public const int MaxSizeLimitKilobytes = 1024 * 1024;

	/// <summary>The <see cref="DeleteAfterDays"/> range: 0 keeps every file, and the top is ten years.</summary>
	public const int MinDeleteAfterDays = 0;

	/// <inheritdoc cref="MinDeleteAfterDays"/>
	public const int MaxDeleteAfterDays = 3650;

	/// <summary>The <see cref="KeepAtMostMegabytes"/> range: 0 sets no cap, and the top is a terabyte.</summary>
	public const int MinKeepAtMostMegabytes = 0;

	/// <inheritdoc cref="MinKeepAtMostMegabytes"/>
	public const int MaxKeepAtMostMegabytes = 1024 * 1024;

	/// <summary>The folder rule as a hint for the field.</summary>
	public const string FolderRequirement = "Enter a full folder path.";

	/// <summary>
	/// Record every session opened from here on. A session that is already open keeps doing whatever its toolbar button
	/// says, so turning this off never cuts a recording short and turning it on never starts five at once.
	/// </summary>
	public bool RecordEverySession { get; init; }

	/// <summary>Where the files go. Empty means the folder <see cref="FolderOrDefault"/> picks under the data directory.</summary>
	[MachineSpecific]
	public string Folder { get; init; } = "";

	/// <summary>How much of one session reaches its file. Past this the file is closed with a note instead of filling the disk.</summary>
	public int SizeLimitKilobytes { get; init; } = 8 * 1024;

	public SessionLogFormat Format { get; init; } = SessionLogFormat.PlainText;

	/// <summary>
	/// Log files last written more than this many days ago are deleted when the app starts and whenever a recording
	/// closes. 0, the default, keeps them for good: nothing is ever deleted unless the user turns this on.
	/// </summary>
	public int DeleteAfterDays { get; init; }

	/// <summary>
	/// When the log files together take more than this many megabytes, the oldest are deleted until the rest fit. 0, the
	/// default, sets no cap.
	/// </summary>
	public int KeepAtMostMegabytes { get; init; }

	/// <summary>True when either rule may delete a file.</summary>
	public bool DeletesOldLogs => DeleteAfterDays > 0 || KeepAtMostMegabytes > 0;

	/// <summary>A copy with every number inside the range the settings page offers.</summary>
	public SessionLogSettings Clamped() => this with
	{
		SizeLimitKilobytes = Math.Clamp(SizeLimitKilobytes, MinSizeLimitKilobytes, MaxSizeLimitKilobytes),
		DeleteAfterDays = Math.Clamp(DeleteAfterDays, MinDeleteAfterDays, MaxDeleteAfterDays),
		KeepAtMostMegabytes = Math.Clamp(KeepAtMostMegabytes, MinKeepAtMostMegabytes, MaxKeepAtMostMegabytes),
	};

	/// <summary>
	/// <paramref name="value"/> trimmed when it is a fully qualified path, otherwise empty. The one rule for the folder:
	/// the settings page refuses what this drops, and a hand-edited settings.json falls back to the default folder
	/// instead of writing session output somewhere nobody meant.
	/// </summary>
	public static string NormalizeFolder(string? value)
	{
		string folder = value?.Trim() ?? "";
		return folder.Length == 0 || folder.Any(char.IsControl) || !Path.IsPathFullyQualified(folder) ? "" : folder;
	}

	/// <summary>
	/// The folder the files actually go in: <see cref="Folder"/> when it names one, otherwise <c>logs/sessions</c> under
	/// <paramref name="dataDirectory"/>, beside the crash logs.
	/// </summary>
	public string FolderOrDefault(string dataDirectory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
		string folder = NormalizeFolder(Folder);
		return folder.Length > 0 ? folder : Path.Combine(dataDirectory, "logs", "sessions");
	}
}
