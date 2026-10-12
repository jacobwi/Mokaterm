using Moka.Red.Feedback.Dialog;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Editing;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.FileBrowser.Browsing;
using Mokaterm.UI.FileBrowser.Dialogs;

namespace Mokaterm.UI.FileBrowser.Editing;

/// <summary>
/// The one editing flow, behind the file browser's F4 and Shift+F4 and behind every module that edits a remote file:
/// read under the cap from <see cref="FileTransferSettings.MaxEditKilobytes"/>, refuse what could not be handed back
/// unchanged, note what the server reported right after the read, show <see cref="FileEditDialog"/> and save through
/// <see cref="RemoteFileEdit"/>. Anything the editor could not hand back unchanged is refused up front rather than
/// opened and quietly rewritten on save.
/// </summary>
internal sealed class RemoteTextEditor : IRemoteTextEditor
{
	private readonly FileBrowserDialogs _dialogs;
	private readonly IUserInteraction _interaction;
	private readonly ISettingsService _settings;

	public RemoteTextEditor(IMokaDialogService dialogs, IUserInteraction interaction, ISettingsService settings)
	{
		_dialogs = new FileBrowserDialogs(dialogs);
		_interaction = interaction;
		_settings = settings;
	}

	public async Task<RemoteTextEditResult> EditAsync(RemoteTextEditRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		IRemoteFileSystem fileSystem = request.FileSystem;
		string path = request.Path;
		string name = request.DisplayName is { Length: > 0 } displayName ? displayName : RemotePath.GetName(path);
		int maxBytes = _settings.Get<FileTransferSettings>().Clamped().MaxEditKilobytes * 1024;

		RemoteTextFile? file;
		TextFileRefusal refusal;
		RemoteFileEntry? opened;
		try
		{
			if (request.CreateNew)
			{
				// Without overwrite, so a name taken since the caller last looked is refused by the server.
				await fileSystem.UploadAsync(path, Stream.Null, UploadOptions.Default, progress: null, cancellationToken);
			}

			await using (Stream source = await fileSystem.OpenReadAsync(path, cancellationToken))
			{
				(file, refusal) = await RemoteTextFile.ReadAsync(source, maxBytes, cancellationToken);
			}

			// Taken after the read, so a change made while it ran shows up at the first save instead of being written over.
			opened = file is null ? null : await fileSystem.StatAsync(path, cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return RemoteTextEditResult.Closed;
		}
		catch (Exception ex)
		{
			return RemoteTextEditResult.Failed(ex, RemoteErrorText.Describe(ex));
		}

		if (file is null)
		{
			_interaction.Notify(NoticeSeverity.Warning, RefusalText(refusal, maxBytes), "Cannot edit " + name);
			return RemoteTextEditResult.Refused(refusal);
		}

		RemoteFileEdit edit = new(fileSystem, path, file, opened, maxBytes);
		bool saved = await _dialogs.EditFileAsync(
			name,
			path,
			file.Text,
			RemoteFileEdit.Facts(file, opened?.Size),
			(text, overwrite) => edit.SaveAsync(text, overwrite, cancellationToken),
			edit.AcceptServerCopy,
			() => ConfirmDiscardAsync(cancellationToken));
		return saved ? RemoteTextEditResult.Saved : RemoteTextEditResult.Closed;
	}

	internal static string RefusalText(TextFileRefusal refusal, int maxBytes) => refusal switch
	{
		TextFileRefusal.TooLarge =>
			$"It is bigger than {DisplayFormat.Bytes(maxBytes)}, the largest the editor opens. The limit is in Settings, Files and transfers.",
		TextFileRefusal.Binary =>
			"It holds bytes that are not text, so an editor would corrupt it.",
		_ =>
			"It is not UTF-8 text. Editing it here would write the editor's own characters back over the server's bytes.",
	};

	private async Task<bool> ConfirmDiscardAsync(CancellationToken cancellationToken)
	{
		ConfirmPrompt prompt = new()
		{
			Title = "Discard changes",
			Message = "The edits have not been saved. Closing the editor throws them away.",
			ConfirmText = "Discard",
			Destructive = true,
		};
		return (await _interaction.ConfirmAsync(prompt, cancellationToken)).Confirmed;
	}
}
