namespace Mokaterm.Abstractions.Settings;

public enum FileBrowserPlacement
{
	Left,
	Right,
}

public enum OverwriteBehavior
{
	Ask,
	Overwrite,
	Skip,
}

/// <summary>File browser and transfer behaviour for every protocol with a file system.</summary>
public sealed record FileTransferSettings : ISettingsSection
{
	/// <summary>The <see cref="MaxConcurrentTransfers"/> range: one at a time up to ten.</summary>
	public const int MinParallelTransfers = 1;

	/// <inheritdoc cref="MinParallelTransfers"/>
	public const int MaxParallelTransfers = 10;

	/// <summary>
	/// The <see cref="MaxEditKilobytes"/> range. A text area holds the whole file in the page, so the top of this range
	/// is about where editing stops feeling immediate rather than about what can be read.
	/// </summary>
	public const int MinEditKilobytes = 16;

	/// <inheritdoc cref="MinEditKilobytes"/>
	public const int MaxEditKilobytesLimit = 4096;

	/// <summary>
	/// The smallest upload <see cref="ResumeTransfers"/> applies to. A smaller one starts over after a failure: sending it
	/// again costs less than the partial file it would leave on the server when nobody retries.
	/// </summary>
	public const long MinResumableBytes = 1024 * 1024;

	public static string SectionKey => "files";

	/// <summary>Open the file browser next to the terminal when a session has both (SSH).</summary>
	public bool OpenWithTerminal { get; init; } = true;

	public FileBrowserPlacement Placement { get; init; } = FileBrowserPlacement.Left;

	public bool ShowHiddenFiles { get; init; }

	public bool FoldersFirst { get; init; } = true;

	/// <summary>
	/// Show a confirmation with the target folder and run-as user before uploading dropped files. The question offers
	/// "Don't ask again", which turns this off; a drop that would write as root asks either way, because the files then
	/// belong to root.
	/// </summary>
	public bool ConfirmDrop { get; init; } = true;

	public bool ConfirmDelete { get; init; } = true;

	public OverwriteBehavior Overwrite { get; init; } = OverwriteBehavior.Ask;

	public bool PreserveTimestamps { get; init; } = true;

	/// <summary>
	/// Compare every finished transfer with the checksum the server computes, and fail it when they differ. Costs the server
	/// one read of the file; a server that cannot compute checksums leaves the copy marked unchecked.
	/// </summary>
	public bool VerifyTransfers { get; init; } = true;

	/// <summary>
	/// Keep what a failed transfer wrote so a retry continues from there instead of starting over. Uploads keep a hidden
	/// partial file beside the target until they finish or are removed from the list; downloads resume on the desktop only.
	/// </summary>
	public bool ResumeTransfers { get; init; } = true;

	public int MaxConcurrentTransfers { get; init; } = 3;

	/// <summary>Desktop only. Null asks where to save every time.</summary>
	[MachineSpecific]
	public string? DownloadDirectory { get; init; }

	/// <summary>
	/// The largest file the built-in editor opens. Past it the file is refused rather than cut, because saving a piece
	/// of a file back would truncate the rest.
	/// </summary>
	public int MaxEditKilobytes { get; init; } = 1024;

	/// <summary>A copy with the transfer count inside the range the settings page offers.</summary>
	public FileTransferSettings Clamped() => this with
	{
		MaxConcurrentTransfers = Math.Clamp(MaxConcurrentTransfers, MinParallelTransfers, MaxParallelTransfers),
		MaxEditKilobytes = Math.Clamp(MaxEditKilobytes, MinEditKilobytes, MaxEditKilobytesLimit),
	};
}
