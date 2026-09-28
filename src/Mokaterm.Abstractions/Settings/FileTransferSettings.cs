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

	public static string SectionKey => "files";

	/// <summary>Open the file browser next to the terminal when a session has both (SSH).</summary>
	public bool OpenWithTerminal { get; init; } = true;

	public FileBrowserPlacement Placement { get; init; } = FileBrowserPlacement.Left;

	public bool ShowHiddenFiles { get; init; }

	public bool FoldersFirst { get; init; } = true;

	/// <summary>Show a confirmation with the target folder and run-as user before uploading dropped files.</summary>
	public bool ConfirmDrop { get; init; } = true;

	public bool ConfirmDelete { get; init; } = true;

	public OverwriteBehavior Overwrite { get; init; } = OverwriteBehavior.Ask;

	public bool PreserveTimestamps { get; init; } = true;

	public int MaxConcurrentTransfers { get; init; } = 3;

	/// <summary>Desktop only. Null asks where to save every time.</summary>
	public string? DownloadDirectory { get; init; }

	/// <summary>A copy with the transfer count inside the range the settings page offers.</summary>
	public FileTransferSettings Clamped() => this with
	{
		MaxConcurrentTransfers = Math.Clamp(MaxConcurrentTransfers, MinParallelTransfers, MaxParallelTransfers),
	};
}
