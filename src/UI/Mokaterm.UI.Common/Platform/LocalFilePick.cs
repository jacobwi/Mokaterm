using Mokaterm.Abstractions.Platform;

namespace Mokaterm.UI.Common.Platform;

/// <summary>
/// Picks one file and reads it, for the editors and dialogs that take a config, a color scheme or a key. Two rules live
/// here rather than at each of them: the pick is released in a finally, because the web host keeps every picked file
/// alive in the page until <see cref="ILocalFileAccess.ReleaseAsync"/> runs, and a file past the cap is refused with a
/// message instead of cut, because a truncated key or scheme looks complete to whatever parses it next.
/// </summary>
public static class LocalFilePick
{
	private const int ChunkBytes = 64 * 1024;

	/// <param name="files">The host's picker.</param>
	/// <param name="maxBytes">The largest file that will be read whole. Anything over it is refused.</param>
	/// <param name="what">What the file was meant to be, for the message: "a private key", "a color scheme".</param>
	/// <param name="cancellationToken">Cancels the pick and the read.</param>
	public static Task<PickedFile<byte[]>> ReadBytesAsync(
		ILocalFileAccess files,
		int maxBytes,
		string what,
		CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
		return ReadAsync(files, maxBytes, what, (stream, token) => ReadBytesAsync(stream, maxBytes, token), cancellationToken);
	}

	/// <param name="files">The host's picker.</param>
	/// <param name="maxCharacters">
	/// The longest text that will be read whole. It caps the file's size in bytes as well, which the host often knows
	/// before anything is opened.
	/// </param>
	/// <param name="what">What the file was meant to be, for the message: "a private key", "a color scheme".</param>
	/// <param name="cancellationToken">Cancels the pick and the read.</param>
	public static Task<PickedFile<string>> ReadTextAsync(
		ILocalFileAccess files,
		int maxCharacters,
		string what,
		CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCharacters);
		return ReadAsync(files, maxCharacters, what, (stream, token) => ReadTextAsync(stream, maxCharacters, token), cancellationToken);
	}

	private static async Task<PickedFile<TContent>> ReadAsync<TContent>(
		ILocalFileAccess files,
		long maxLength,
		string what,
		Func<Stream, CancellationToken, Task<TContent?>> readAsync,
		CancellationToken cancellationToken)
		where TContent : class
	{
		ArgumentNullException.ThrowIfNull(files);
		ArgumentException.ThrowIfNullOrWhiteSpace(what);

		IReadOnlyList<LocalFileItem> picked = await files.PickFilesAsync(multiple: false, cancellationToken);
		try
		{
			if (picked.Count == 0 || picked[0] is not { OpenReadAsync: { } openAsync } file)
			{
				return new PickedFile<TContent>();
			}

			// A host that knows the size says so before the file is opened, so the plain refusal costs no read.
			if (file.Length > maxLength)
			{
				return PickedFile<TContent>.TooLarge(what);
			}

			await using Stream stream = await openAsync(cancellationToken);
			TContent? content = await readAsync(stream, cancellationToken);
			return content is null
				? PickedFile<TContent>.TooLarge(what)
				: new PickedFile<TContent> { Name = file.Name, Content = content };
		}
		finally
		{
			await files.ReleaseAsync(picked);
		}
	}

	private static async Task<byte[]?> ReadBytesAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
	{
		using MemoryStream buffer = new();
		byte[] chunk = new byte[ChunkBytes];
		int read;
		while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
		{
			if (buffer.Length + read > maxBytes)
			{
				return null;
			}

			buffer.Write(chunk, 0, read);
		}

		return buffer.ToArray();
	}

	private static async Task<string?> ReadTextAsync(Stream stream, int maxCharacters, CancellationToken cancellationToken)
	{
		// One character of room past the cap is what tells a file that just fits from one that would have been cut.
		char[] buffer = new char[maxCharacters + 1];
		try
		{
			using StreamReader reader = new(stream);
			int read = await reader.ReadBlockAsync(buffer, cancellationToken);
			return read > maxCharacters ? null : new string(buffer, 0, read);
		}
		finally
		{
			// A picked file can be a private key, and this buffer would otherwise sit on the heap until a GC ran.
			Array.Clear(buffer);
		}
	}
}
