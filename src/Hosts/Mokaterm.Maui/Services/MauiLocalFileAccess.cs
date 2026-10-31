using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Maui.Services;

/// <summary>Native pickers for uploads and downloads. Folder picking and save dialogs are per platform.</summary>
internal sealed partial class MauiLocalFileAccess : ILocalFileAccess
{
	/// <summary>What a download is called until it is complete, so a partial file never passes for a finished one.</summary>
	private const string PartialSuffix = ".part";

	private const int WriteBufferSize = 128 * 1024;

	private readonly ISettingsService _settings;

	public MauiLocalFileAccess(ISettingsService settings) => _settings = settings;

	public bool CanPickFolders => PlatformCanPickFolders;

	public bool CanResumeSaves => true;

	public async ValueTask<IReadOnlyList<LocalFileItem>> PickFilesAsync(bool multiple = true, IReadOnlyList<string>? extensions = null, CancellationToken cancellationToken = default)
	{
		List<string> paths = await MainThread.InvokeOnMainThreadAsync(async () =>
		{
			PickOptions options = new()
			{
				PickerTitle = multiple ? "Choose files to upload" : "Choose a file",
				// The desktop host is Windows, so one platform's list is enough; a host that cannot filter ignores it.
				FileTypes = extensions is { Count: > 0 } ? new FilePickerFileType(
					new Dictionary<DevicePlatform, IEnumerable<string>> { [DevicePlatform.WinUI] = extensions }) : null,
			};
			if (multiple)
			{
				IEnumerable<FileResult?>? results = await FilePicker.Default.PickMultipleAsync(options);
				return results is null ? new List<string>() : results.OfType<FileResult>().Select(result => result.FullPath).ToList();
			}

			FileResult? single = await FilePicker.Default.PickAsync(options);
			return single is null ? new List<string>() : new List<string> { single.FullPath };
		});

		return LocalPaths.Expand(paths);
	}

	public async ValueTask<IReadOnlyList<LocalFileItem>> PickFolderAsync(CancellationToken cancellationToken = default)
	{
		string? folder = await MainThread.InvokeOnMainThreadAsync(PickFolderPathAsync);
		return folder is null ? [] : LocalPaths.Expand([folder]);
	}

	public async ValueTask<bool> SaveFileAsync(string suggestedName, long? length, Func<Stream, CancellationToken, Task> writeAsync, CancellationToken cancellationToken = default)
	{
		string safeName = SanitizeFileName(suggestedName);
		string? directory = _settings.Get<FileTransferSettings>().DownloadDirectory;

		string? target = !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
			? UniquePath(Path.Combine(directory, safeName))
			: await MainThread.InvokeOnMainThreadAsync(() => PickSavePathAsync(safeName));

		if (target is null)
		{
			return false;
		}

		try
		{
			await using (FileStream stream = new(target, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous))
			{
				await writeAsync(stream, cancellationToken);
			}

			return true;
		}
		catch
		{
			// A half-written download looks like a good file later; remove it.
			TryDelete(target);
			throw;
		}
	}

	/// <summary>
	/// Writes into a .part file beside where the download goes and gives it the real name once it is complete. A failed
	/// attempt keeps the .part file, and the next one opens it again and carries on after its last byte.
	/// </summary>
	public async ValueTask<bool> SaveFileAsync(LocalSave save, Func<Stream, long, CancellationToken, Task> writeAsync, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(save);
		ArgumentNullException.ThrowIfNull(writeAsync);

		// A .part file left by something else must not pass for this download's beginning, so the first attempt starts empty.
		bool firstAttempt = save.PartialPath is null || save.TargetPath is null;
		if (save.TargetPath is null)
		{
			string safeName = SanitizeFileName(save.SuggestedName);
			string? directory = _settings.Get<FileTransferSettings>().DownloadDirectory;
			if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
			{
				save.TargetPath = UniquePath(Path.Combine(directory, safeName));
			}
			else
			{
				// The save dialog already asked about replacing an existing file.
				save.TargetPath = await MainThread.InvokeOnMainThreadAsync(() => PickSavePathAsync(safeName));
				save.ReplaceTarget = true;
			}

			if (save.TargetPath is null)
			{
				return false;
			}
		}

		string partial = save.PartialPath ??= save.TargetPath + PartialSuffix;
		FileStream stream = new(partial, firstAttempt ? FileMode.Create : FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, WriteBufferSize, FileOptions.Asynchronous);
		await using (stream)
		{
			long existing = stream.Length;
			stream.Seek(existing, SeekOrigin.Begin);
			await writeAsync(stream, existing, cancellationToken);
		}

		string target = save.TargetPath;
		if (!save.ReplaceTarget && File.Exists(target))
		{
			// Something took the name in the download folder while this ran; the download is not the one to lose.
			target = UniquePath(target);
		}

		File.Move(partial, target, overwrite: save.ReplaceTarget);
		save.TargetPath = target;
		save.PartialPath = null;
		return true;
	}

	public ValueTask DiscardAsync(LocalSave save)
	{
		ArgumentNullException.ThrowIfNull(save);
		if (save.PartialPath is { } partial)
		{
			TryDelete(partial);
			save.PartialPath = null;
		}

		return ValueTask.CompletedTask;
	}

	private static string SanitizeFileName(string name)
	{
		string cleaned = string.Concat(name.Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c)).Trim();
		return cleaned.Length == 0 ? "download" : cleaned;
	}

	private static string UniquePath(string path)
	{
		if (!File.Exists(path) && !File.Exists(path + PartialSuffix))
		{
			return path;
		}

		string directory = Path.GetDirectoryName(path)!;
		string stem = Path.GetFileNameWithoutExtension(path);
		string extension = Path.GetExtension(path);
		for (int i = 1; ; i++)
		{
			string candidate = Path.Combine(directory, $"{stem} ({i}){extension}");
			if (!File.Exists(candidate) && !File.Exists(candidate + PartialSuffix))
			{
				return candidate;
			}
		}
	}

	private static void TryDelete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}
}
