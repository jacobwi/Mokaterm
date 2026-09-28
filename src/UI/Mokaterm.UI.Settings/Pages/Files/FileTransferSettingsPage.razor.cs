using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.UI.Settings.Pages.Files;

/// <summary>Settings page for <see cref="FileTransferSettings"/>.</summary>
public sealed partial class FileTransferSettingsPage : SettingsSectionBase<FileTransferSettings>
{
	private const string PageTitle = "Files & transfers";

	private static readonly IReadOnlyList<EnumOption<FileBrowserPlacement>> PlacementOptions =
	[
		new(FileBrowserPlacement.Left, "Left"),
		new(FileBrowserPlacement.Right, "Right"),
	];

	private static readonly IReadOnlyList<EnumOption<OverwriteBehavior>> OverwriteOptions =
	[
		new(OverwriteBehavior.Ask, "Ask"),
		new(OverwriteBehavior.Overwrite, "Overwrite"),
		new(OverwriteBehavior.Skip, "Skip"),
	];

	[Inject]
	private IAppEnvironment AppEnvironment { get; set; } = default!;

	private static string? ValidateDirectory(string value) =>
		Path.IsPathFullyQualified(value) ? null : "Enter a full folder path.";

	// The slider reports a double; the range is the section's.
	private Task SetMaxConcurrentTransfersAsync(double value) =>
		SaveAsync(s => s with { MaxConcurrentTransfers = (int)Math.Round(value) });

	private Task SetDownloadDirectoryAsync(string value)
	{
		string? directory = value.Length == 0 ? null : value;
		return SaveAsync(s => s with { DownloadDirectory = directory });
	}

	private Task SaveAsync(Func<FileTransferSettings, FileTransferSettings> change) => UpdateAsync(s => change(s).Clamped());
}
