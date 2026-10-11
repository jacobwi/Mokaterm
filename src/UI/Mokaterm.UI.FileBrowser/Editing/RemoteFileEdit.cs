using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.FileBrowser.Browsing;

namespace Mokaterm.UI.FileBrowser.Editing;

/// <summary>
/// One remote file open in the editor, between reading it and writing it back. It holds the format the file goes back
/// in and what the server reported when the file was last read, which every save checks first, so a change someone
/// else made meanwhile is put in front of the user instead of being written over.
/// </summary>
internal sealed class RemoteFileEdit
{
	private readonly IRemoteFileSystem _fileSystem;
	private readonly string _path;
	private readonly int _maxBytes;

	// False when the stat at open found nothing, which for a file that was just read means this file system cannot be
	// asked. Comparing anyway would make every save look like a conflict.
	private readonly bool _tracked;

	private RemoteTextFile _file;
	private RemoteFileEntry? _stamp;

	// The copy the last conflict read, kept until the user goes back to editing on top of it or saves over it.
	private bool _holding;
	private RemoteTextFile? _heldFile;
	private RemoteFileEntry? _heldStamp;

	/// <param name="stamp">What a stat returned right after <paramref name="file"/> was read.</param>
	/// <param name="maxBytes">The editor's size cap, which a server copy read for a conflict is held to as well.</param>
	public RemoteFileEdit(IRemoteFileSystem fileSystem, string path, RemoteTextFile file, RemoteFileEntry? stamp, int maxBytes)
	{
		_fileSystem = fileSystem;
		_path = path;
		_file = file;
		_stamp = stamp;
		_maxBytes = maxBytes;
		_tracked = stamp is not null;
	}

	/// <summary>
	/// Writes <paramref name="text"/> in the file's own format. Unless <paramref name="overwrite"/> is set, a file that
	/// changed on the server since it was last read is not written: the answer is a conflict with the server's copy.
	/// </summary>
	public async Task<FileSaveResult> SaveAsync(string text, bool overwrite, CancellationToken cancellationToken)
	{
		try
		{
			if (!overwrite && _tracked)
			{
				RemoteFileEntry? current = await _fileSystem.StatAsync(_path, cancellationToken);
				if (Moved(current))
				{
					return await ReadServerCopyAsync(current, cancellationToken);
				}
			}

			using MemoryStream body = new(_file.ToBytes(text));
			await _fileSystem.UploadAsync(_path, body, new UploadOptions { Overwrite = true }, progress: null, cancellationToken);
			Release();
			return FileSaveResult.Saved;
		}
		catch (OperationCanceledException)
		{
			return FileSaveResult.Failed("The connection went away before the file was written.");
		}
		catch (Exception ex)
		{
			return FileSaveResult.Failed(RemoteErrorText.Describe(ex));
		}
	}

	/// <summary>
	/// Makes the copy the last conflict read the new baseline: the next save compares against what the server reported
	/// for it, and writes in its format when it was text. Does nothing while no conflict is waiting.
	/// </summary>
	public void AcceptServerCopy()
	{
		if (!_holding)
		{
			return;
		}

		_stamp = _heldStamp;
		if (_heldFile is not null)
		{
			_file = _heldFile;
		}

		Release();
	}

	/// <summary>Size, encoding and line ending, one chip each, in the order the editor shows them.</summary>
	public static string[] Facts(RemoteTextFile file, long? length)
	{
		string ending = file.LineEnding switch
		{
			"\r\n" => "CRLF",
			"\r" => "CR",
			_ => "LF",
		};
		string encoding = file.HasBom ? "UTF-8 BOM" : "UTF-8";
		return length is { } bytes ? [DisplayFormat.Bytes(bytes), encoding, ending] : [encoding, ending];
	}

	/// <summary>
	/// Size and modification time together: either moving is someone else writing the file, and so is the file going
	/// away or coming back.
	/// </summary>
	private bool Moved(RemoteFileEntry? current)
	{
		if (_stamp is null || current is null)
		{
			return _stamp is not null || current is not null;
		}

		return _stamp.Size != current.Size || _stamp.LastModified != current.LastModified;
	}

	private async Task<FileSaveResult> ReadServerCopyAsync(RemoteFileEntry? current, CancellationToken cancellationToken)
	{
		if (current is null)
		{
			Hold(null, null);
			return FileSaveResult.UnreadableConflict("It is no longer on the server.");
		}

		RemoteTextFile? copy;
		TextFileRefusal refusal;
		RemoteFileEntry? stamp;
		try
		{
			await using (Stream source = await _fileSystem.OpenReadAsync(_path, cancellationToken))
			{
				(copy, refusal) = await RemoteTextFile.ReadAsync(source, _maxBytes, cancellationToken);
			}

			// Taken after the read, like the one at open, so a change while it ran shows up at the next save.
			stamp = await _fileSystem.StatAsync(_path, cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Hold(null, current);
			return FileSaveResult.UnreadableConflict("It changed on the server, and reading the new copy failed: " + RemoteErrorText.Describe(ex));
		}

		Hold(copy, stamp);
		if (copy is null)
		{
			return FileSaveResult.UnreadableConflict(RefusalText(refusal));
		}

		return FileSaveResult.Conflict(copy.Text, Facts(copy, stamp?.Size ?? current.Size));
	}

	private string RefusalText(TextFileRefusal refusal) => refusal switch
	{
		TextFileRefusal.TooLarge =>
			$"It changed on the server and is now bigger than {DisplayFormat.Bytes(_maxBytes)}, the largest the editor reads, so the two cannot be compared.",
		TextFileRefusal.Binary =>
			"It changed on the server and now holds bytes that are not text, so the two cannot be compared.",
		_ =>
			"It changed on the server and is no longer UTF-8 text, so the two cannot be compared.",
	};

	private void Hold(RemoteTextFile? file, RemoteFileEntry? stamp)
	{
		_holding = true;
		_heldFile = file;
		_heldStamp = stamp;
	}

	private void Release()
	{
		_holding = false;
		_heldFile = null;
		_heldStamp = null;
	}
}
