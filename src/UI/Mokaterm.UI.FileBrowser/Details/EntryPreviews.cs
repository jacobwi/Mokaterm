using System.Buffers;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.FileBrowser.Browsing;

namespace Mokaterm.UI.FileBrowser.Details;

/// <summary>
/// The reads behind the details pane's preview: the start of a text file, or a small image whole. Nothing else is read,
/// and each read is an ordinary read on the file system it is given, root's included.
/// </summary>
internal static class EntryPreviews
{
	/// <summary>How much of a text file is read: more than the pane shows, and little enough for a slow link.</summary>
	public const int TextBytes = 32 * 1024;

	/// <summary>The largest image read for the pane. The whole file goes to the page, so a bigger one is not read at all.</summary>
	public const int ImageBytes = 2 * 1024 * 1024;

	private const int ChunkBytes = 64 * 1024;

	private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
	{
		[".png"] = "image/png",
		[".jpg"] = "image/jpeg",
		[".jpeg"] = "image/jpeg",
		[".gif"] = "image/gif",
		[".webp"] = "image/webp",
		[".svg"] = "image/svg+xml",
	};

	/// <summary>
	/// Only regular files and links the listing says lead to one: a FIFO or a device would block the read or never end
	/// it, and a link to one says so.
	/// </summary>
	public static EntryPreviewKind KindOf(RemoteFileEntry entry)
	{
		ArgumentNullException.ThrowIfNull(entry);
		bool file = entry.Kind == RemoteEntryKind.File || (entry.Kind == RemoteEntryKind.SymbolicLink && entry.LinkTargetKind == RemoteEntryKind.File);
		if (!file)
		{
			return EntryPreviewKind.None;
		}

		return ImageTypeOf(entry.Name) is null ? EntryPreviewKind.Text : EntryPreviewKind.Image;
	}

	/// <summary>The media type of an image the pane shows, by the name's extension, or null.</summary>
	public static string? ImageTypeOf(string name)
	{
		ArgumentNullException.ThrowIfNull(name);
		int dot = name.LastIndexOf('.');
		return dot > 0 && ImageTypes.TryGetValue(name[dot..], out string? type) ? type : null;
	}

	/// <summary>Reads what the pane shows of <paramref name="entry"/>. A failed read comes back as a preview that says so.</summary>
	/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
	public static async Task<EntryPreview> LoadAsync(IRemoteFileSystem fileSystem, RemoteFileEntry entry, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(fileSystem);
		ArgumentNullException.ThrowIfNull(entry);
		try
		{
			return KindOf(entry) switch
			{
				EntryPreviewKind.Text => await ReadTextAsync(fileSystem, entry, cancellationToken),
				EntryPreviewKind.Image => await ReadImageAsync(fileSystem, entry, ImageTypeOf(entry.Name)!, cancellationToken),
				_ => EntryPreview.None,
			};
		}
		catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
		{
			return EntryPreview.OfFailure(RemoteErrorText.Describe(ex));
		}
	}

	private static async Task<EntryPreview> ReadTextAsync(IRemoteFileSystem fileSystem, RemoteFileEntry entry, CancellationToken cancellationToken)
	{
		Stream source = await fileSystem.OpenReadAsync(entry.Path, cancellationToken);
		await using (source)
		{
			(RemoteTextFile? file, TextFileRefusal refusal) = await RemoteTextFile.ReadStartAsync(source, TextBytes, cancellationToken);
			if (file is null)
			{
				return refusal == TextFileRefusal.NotUtf8 ? EntryPreview.NotUtf8 : EntryPreview.Binary;
			}

			return file.Text.Length == 0 && !file.IsCut
				? EntryPreview.Empty
				: EntryPreview.OfText(TerminalTextFilter.Plain(file.Text), file.IsCut);
		}
	}

	private static async Task<EntryPreview> ReadImageAsync(IRemoteFileSystem fileSystem, RemoteFileEntry entry, string type, CancellationToken cancellationToken)
	{
		// A link lists its own size rather than its target's, so only a plain file can be refused before reading.
		if (entry.Kind == RemoteEntryKind.File && entry.Size > ImageBytes)
		{
			return EntryPreview.TooLarge;
		}

		// One byte past the cap tells a file of exactly the cap from a longer one, since a stream need not know its length.
		const int limit = ImageBytes + 1;
		using MemoryStream image = new();
		byte[] chunk = ArrayPool<byte>.Shared.Rent(ChunkBytes);
		try
		{
			Stream source = await fileSystem.OpenReadAsync(entry.Path, cancellationToken);
			await using (source)
			{
				while (image.Length < limit)
				{
					int want = (int)Math.Min(chunk.Length, limit - image.Length);
					int read = await source.ReadAsync(chunk.AsMemory(0, want), cancellationToken);
					if (read == 0)
					{
						break;
					}

					image.Write(chunk, 0, read);
				}
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(chunk);
		}

		if (image.Length > ImageBytes)
		{
			return EntryPreview.TooLarge;
		}

		return image.Length == 0
			? EntryPreview.Empty
			: EntryPreview.OfImage("data:" + type + ";base64," + Convert.ToBase64String(image.GetBuffer(), 0, (int)image.Length));
	}
}
