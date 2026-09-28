using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;

namespace Mokaterm.Maui.Services;

/// <summary>Native pickers for uploads and downloads. Folder picking and save dialogs are per platform.</summary>
internal sealed partial class MauiLocalFileAccess : ILocalFileAccess
{
	private readonly ISettingsService _settings;

	public MauiLocalFileAccess(ISettingsService settings) => _settings = settings;

	public bool CanPickFolders => PlatformCanPickFolders;

	public async ValueTask<IReadOnlyList<LocalFileItem>> PickFilesAsync(bool multiple = true, CancellationToken cancellationToken = default)
	{
		List<string> paths = await MainThread.InvokeOnMainThreadAsync(async () =>
		{
			PickOptions options = new() { PickerTitle = multiple ? "Choose files to upload" : "Choose a file" };
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

	private static string SanitizeFileName(string name)
	{
		string cleaned = string.Concat(name.Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c)).Trim();
		return cleaned.Length == 0 ? "download" : cleaned;
	}

	private static string UniquePath(string path)
	{
		if (!File.Exists(path))
		{
			return path;
		}

		string directory = Path.GetDirectoryName(path)!;
		string stem = Path.GetFileNameWithoutExtension(path);
		string extension = Path.GetExtension(path);
		for (int i = 1; ; i++)
		{
			string candidate = Path.Combine(directory, $"{stem} ({i}){extension}");
			if (!File.Exists(candidate))
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
