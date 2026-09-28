namespace Mokaterm.Tests.Shared;

/// <summary>A directory under the temp folder for one test, deleted when the test lets go of it.</summary>
internal sealed class TempFolder : IAsyncDisposable
{
	public TempFolder(string prefix = "mokaterm-tests")
	{
		Path = NewPath(prefix);
		Directory.CreateDirectory(Path);
	}

	public string Path { get; }

	/// <summary>A path under the temp folder that nothing has created yet.</summary>
	public static string NewPath(string prefix = "mokaterm-tests") =>
		System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));

	/// <summary>
	/// Deletes <paramref name="path"/>, retrying: on Windows a file the test has just closed can still be held for a
	/// moment by whatever is watching the disk, and the first delete then throws. A folder left behind in the end is
	/// not worth failing a run over.
	/// </summary>
	public static async ValueTask DeleteAsync(string path)
	{
		for (int attempt = 0; attempt < 10 && Directory.Exists(path); attempt++)
		{
			try
			{
				Directory.Delete(path, recursive: true);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				await Task.Delay(50);
			}
		}
	}

	public ValueTask DisposeAsync() => DeleteAsync(Path);
}
