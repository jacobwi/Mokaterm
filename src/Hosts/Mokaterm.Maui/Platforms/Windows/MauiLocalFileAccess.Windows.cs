using Windows.Storage;
using Windows.Storage.Pickers;

namespace Mokaterm.Maui.Services;

internal sealed partial class MauiLocalFileAccess
{
	private const bool PlatformCanPickFolders = true;

	private static async Task<string?> PickFolderPathAsync()
	{
		FolderPicker picker = new() { SuggestedStartLocation = PickerLocationId.Desktop };
		picker.FileTypeFilter.Add("*");
		WinRT.Interop.InitializeWithWindow.Initialize(picker, Platforms.Windows.WindowHandles.Current());

		StorageFolder? folder = await picker.PickSingleFolderAsync();
		return folder?.Path;
	}

	private static async Task<string?> PickSavePathAsync(string suggestedName)
	{
		FileSavePicker picker = new()
		{
			SuggestedStartLocation = PickerLocationId.Downloads,
			SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName),
		};

		string extension = Path.GetExtension(suggestedName);
		picker.FileTypeChoices.Add(
			extension.Length > 1 ? extension[1..].ToUpperInvariant() + " file" : "File",
			[extension.Length > 1 ? extension : "."]);

		WinRT.Interop.InitializeWithWindow.Initialize(picker, Platforms.Windows.WindowHandles.Current());

		StorageFile? file = await picker.PickSaveFileAsync();
		return file?.Path;
	}
}
