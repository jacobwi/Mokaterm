using Moka.Red.Feedback.Dialog;
using Mokaterm.UI.Common.Files;
using Mokaterm.UI.Common.Icons;

namespace Mokaterm.UI.FileBrowser.Dialogs;

/// <summary>
/// Opens <see cref="FileBrowserDialog"/> through the page's own dialog service, so the browser shows in the window that
/// asked, and the browser's own dialogs (rename, the editor, properties) stack on top of it.
/// </summary>
internal sealed class FileBrowserDialogService : IFileBrowserDialog
{
	// A file browser wants room for its columns and a long list, and keeps that size whatever folder it shows.
	private const string Width = "min(1000px, calc(100vw - 32px))";
	private const string Height = "min(760px, calc(100vh - 96px))";

	private readonly IMokaDialogService _dialogs;

	public FileBrowserDialogService(IMokaDialogService dialogs) => _dialogs = dialogs;

	public async Task ShowAsync(FileBrowserRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (cancellationToken.IsCancellationRequested)
		{
			return;
		}

		_ = await OpenAsync(request, pickFolder: false, cancellationToken);
	}

	public async Task<string?> PickFolderAsync(FileBrowserRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (cancellationToken.IsCancellationRequested)
		{
			return null;
		}

		return await OpenAsync(request, pickFolder: true, cancellationToken) as string;
	}

	private Task<object?> OpenAsync(FileBrowserRequest request, bool pickFolder, CancellationToken cancellationToken) =>
		_dialogs.ShowComponentAsync<FileBrowserDialog>(
			request.Title,
			parameters =>
			{
				parameters[nameof(FileBrowserDialog.Request)] = request;
				parameters[nameof(FileBrowserDialog.Closing)] = cancellationToken;
				parameters[nameof(FileBrowserDialog.PickFolder)] = pickFolder;
			},
			options =>
			{
				options.Icon = MokatermIcons.FolderOpen;
				options.Size = MokaDialogSize.Large;
				options.Width = Width;
				options.MaxWidth = Width;
				options.Height = Height;
				options.BodyPadding = false;
				options.ScrollBody = false;

				// Escape clears the browser's filter and leaves its path box, and the dialog would take every one of them.
				options.CloseOnEscape = false;
				options.CloseOnBackdropClick = false;
			});
}
