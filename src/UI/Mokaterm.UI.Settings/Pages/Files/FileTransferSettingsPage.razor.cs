using Microsoft.AspNetCore.Components;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Formatting;

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

	private string ResumeDescription =>
		"A retry continues where the failed attempt stopped. Uploads of " + DisplayFormat.Bytes(FileTransferSettings.MinResumableBytes)
		+ " and more keep a hidden partial file on the server until they finish or leave the transfer list"
		+ (AppEnvironment.Kind == HostKind.Desktop
			? ", and downloads keep a .part file beside where they are saved."
			: ". Downloads start over, because a browser cannot continue one.");

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
