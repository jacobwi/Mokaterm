using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.FileBrowser.Browsing;
using Mokaterm.UI.FileBrowser.Editing;

namespace Mokaterm.UI.FileBrowser;

/// <summary>Opening a remote text file in the built-in editor and writing it back in place.</summary>
public sealed partial class RemoteFileBrowser
{
	/// <summary>
	/// Reads the file, shows the editor, and saves through the file system's own replace, which keeps the owner and
	/// mode the file already had. Anything the editor could not hand back unchanged is refused up front rather than
	/// opened and quietly rewritten on save.
	/// </summary>
	private async Task EditAsync(RemoteFileEntry entry)
	{
		if (_fileSystem is not { } fileSystem || entry.IsDirectoryLike)
		{
			return;
		}

		CancellationToken token = ConnectionToken;
		int maxBytes = Settings.Get<FileTransferSettings>().Clamped().MaxEditKilobytes * 1024;

		RemoteTextFile? file;
		TextFileRefusal refusal;
		RemoteFileEntry? opened;
		try
		{
			await using Stream source = await fileSystem.OpenReadAsync(entry.Path, token);
			(file, refusal) = await RemoteTextFile.ReadAsync(source, maxBytes, token);

			// What the file looked like when it was read, so a save can notice someone else changing it meanwhile.
			opened = file is null ? null : await fileSystem.StatAsync(entry.Path, token);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			ShowAlert("Could not open " + entry.Name, ex, retryAsRoot: _ => EditAsync(entry));
			return;
		}
		catch (OperationCanceledException)
		{
			return;
		}

		if (file is null)
		{
			Interaction.Notify(NoticeSeverity.Warning, RefusalText(refusal, maxBytes), "Cannot edit " + entry.Name);
			return;
		}

		bool saved = await Dialogs.EditFileAsync(
			entry.Name,
			entry.Path,
			file.Text,
			Facts(file, opened?.Size ?? entry.Size),
			text => SaveEditAsync(fileSystem, entry, opened, file, text),
			ConfirmDiscardEditAsync);

		if (saved)
		{
			await RefreshAsync(entry.Path);
		}
	}

	private async Task<string?> SaveEditAsync(
		IRemoteFileSystem fileSystem,
		RemoteFileEntry entry,
		RemoteFileEntry? opened,
		RemoteTextFile file,
		string text)
	{
		CancellationToken token = ConnectionToken;
		try
		{
			if (opened is not null && await fileSystem.StatAsync(entry.Path, token) is { } current && ChangedSince(opened, current))
			{
				ConfirmPrompt prompt = new()
				{
					Title = "Changed on the server",
					Message = $"{entry.Name} has changed since it was opened. Saving replaces what is there now.",
					ConfirmText = "Save anyway",
					Destructive = true,
				};
				if (!(await Interaction.ConfirmAsync(prompt, token)).Confirmed)
				{
					return "It changed on the server since it was opened, so nothing was written.";
				}
			}

			using MemoryStream body = new(file.ToBytes(text));
			await fileSystem.UploadAsync(entry.Path, body, new UploadOptions { Overwrite = true }, progress: null, token);
			return null;
		}
		catch (OperationCanceledException)
		{
			return "The connection went away before the file was written.";
		}
		catch (Exception ex)
		{
			return RemoteErrorText.Describe(ex);
		}
	}

	private async Task<bool> ConfirmDiscardEditAsync()
	{
		ConfirmPrompt prompt = new()
		{
			Title = "Discard changes",
			Message = "The edits have not been saved. Closing the editor throws them away.",
			ConfirmText = "Discard",
			Destructive = true,
		};
		return (await Interaction.ConfirmAsync(prompt, ConnectionToken)).Confirmed;
	}

	/// <summary>Size and modification time together: either moving is someone else writing the file.</summary>
	private static bool ChangedSince(RemoteFileEntry opened, RemoteFileEntry current) =>
		opened.Size != current.Size || opened.LastModified != current.LastModified;

	private static string Facts(RemoteTextFile file, long? length)
	{
		string ending = file.LineEnding switch
		{
			"\r\n" => "CRLF",
			"\r" => "CR",
			_ => "LF",
		};
		string encoding = file.HasBom ? "UTF-8 BOM" : "UTF-8";
		return length is { } bytes
			? string.Join(" · ", DisplayFormat.Bytes(bytes), encoding, ending)
			: string.Join(" · ", encoding, ending);
	}

	private static string RefusalText(TextFileRefusal refusal, int maxBytes) => refusal switch
	{
		TextFileRefusal.TooLarge =>
			$"It is bigger than {DisplayFormat.Bytes(maxBytes)}, the largest the editor opens. Download it, or raise the limit in Settings, Files and transfers.",
		TextFileRefusal.Binary =>
			"It holds bytes that are not text, so an editor would corrupt it. Download it instead.",
		_ =>
			"It is not UTF-8 text. Editing it here would write the editor's own characters back over the server's bytes, so download it instead.",
	};
}
