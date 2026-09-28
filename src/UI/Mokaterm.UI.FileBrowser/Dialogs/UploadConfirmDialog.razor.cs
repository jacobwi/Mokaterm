using System.Globalization;
using Microsoft.AspNetCore.Components;
using Moka.Red.Feedback.Dialog;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.FileBrowser.Transfers;

namespace Mokaterm.UI.FileBrowser.Dialogs;

/// <summary>
/// Confirms dropped files before they upload: how much, where to, and whether as the logged-in user or as root.
/// Enter confirms. Closes with the chosen account.
/// </summary>
public sealed partial class UploadConfirmDialog : ComponentBase
{
	private const string UserValue = "user";
	private const string RootValue = "root";

	private bool _asRoot;

	[CascadingParameter]
	public MokaDialogContext? Dialog { get; set; }

	[Parameter]
	public int FileCount { get; set; }

	[Parameter]
	public int FolderCount { get; set; }

	/// <summary>Combined size of the files; null when a size is unknown.</summary>
	[Parameter]
	public long? TotalBytes { get; set; }

	/// <summary>The remote folder the files go to.</summary>
	[Parameter]
	public string Directory { get; set; } = "";

	[Parameter]
	public string Host { get; set; } = "";

	/// <summary>The logged-in account.</summary>
	[Parameter]
	public string UserName { get; set; } = "";

	[Parameter]
	public bool SupportsElevation { get; set; }

	/// <summary>Preselects root, which follows the browser's current mode.</summary>
	[Parameter]
	public bool AsRoot { get; set; }

	private string RunAsValue => _asRoot ? RootValue : UserValue;

	private string Destination => TransferOrigin.Format(_asRoot ? "root" : UserName, Host, Directory);

	private string CountText => (FileCount, FolderCount) switch
	{
		(1, 0) => "1 file",
		(_, 0) => string.Create(CultureInfo.CurrentCulture, $"{FileCount:N0} files"),
		(_, 1) => string.Create(CultureInfo.CurrentCulture, $"{FileCount:N0} {(FileCount == 1 ? "file" : "files")} in 1 folder"),
		_ => string.Create(CultureInfo.CurrentCulture, $"{FileCount:N0} {(FileCount == 1 ? "file" : "files")} in {FolderCount:N0} folders"),
	};

	private string SizeText => TotalBytes is { } bytes ? DisplayFormat.Bytes(bytes) : "Size unknown";

	protected override void OnInitialized() => _asRoot = SupportsElevation && AsRoot;

	private void OnRunAsChanged(string value) => _asRoot = SupportsElevation && value == RootValue;

	private void Confirm() => Dialog?.Close(_asRoot ? UploadRunAs.Root : UploadRunAs.User);

	private void Cancel() => Dialog?.Cancel();
}
