using Mokaterm.Abstractions.Storage;

namespace Mokaterm.Core.Storage;

/// <summary>Replaces files so a crash leaves either the old or the new content, never a mix.</summary>
internal static class AtomicFile
{
	private const int MoveAttempts = 5;

	public static async Task WriteAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
	{
		string directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("The path has no directory.", nameof(path));
		OwnerOnlyFiles.CreateDirectory(directory);

		// Same directory as the target: File.Move is only a rename (and atomic) within one volume.
		string tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
		try
		{
			FileStreamOptions options = OwnerOnlyFiles.Owned(new FileStreamOptions
			{
				Mode = FileMode.CreateNew,
				Access = FileAccess.Write,
				Share = FileShare.None,
				Options = FileOptions.Asynchronous,
			});

			await using (FileStream stream = new(tempPath, options))
			{
				await stream.WriteAsync(content, cancellationToken);
				stream.Flush(flushToDisk: true);
			}

			await MoveWithRetryAsync(tempPath, path, cancellationToken);
		}
		catch
		{
			TryDelete(tempPath);
			throw;
		}
	}

	public static void TryDelete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// Leftover temp files are harmless; the next write uses a new name.
		}
	}

	private static async Task MoveWithRetryAsync(string source, string destination, CancellationToken cancellationToken)
	{
		for (int attempt = 1; ; attempt++)
		{
			try
			{
				File.Move(source, destination, overwrite: true);
				return;
			}
			catch (Exception ex) when (attempt < MoveAttempts && ex is IOException or UnauthorizedAccessException)
			{
				// Windows refuses to replace a file that a scanner or indexer briefly holds open.
				await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken);
			}
		}
	}
}
