using System.Buffers;
using System.IO.Compression;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.FileBrowser.Transfers;

/// <summary>One archive entry: a folder (name ends with '/') or a file read from <see cref="Entry"/>.</summary>
internal sealed record ZipItem(string EntryName, RemoteFileEntry Entry, bool IsDirectory);

/// <summary>Streams remote folders and files into a zip archive, one read buffer at a time.</summary>
internal static class RemoteZipWriter
{
	private const int BufferSize = 81920;

	/// <summary>
	/// Lists what the archive will hold. Links to folders inside the tree are not followed, since they can loop, and
	/// devices, sockets and pipes are skipped because reading one can block forever.
	/// </summary>
	public static async Task<IReadOnlyList<ZipItem>> PlanAsync(
		IRemoteFileSystem fileSystem,
		IReadOnlyList<RemoteFileEntry> roots,
		CancellationToken cancellationToken)
	{
		List<ZipItem> items = [];
		foreach (RemoteFileEntry root in roots)
		{
			if (root.IsDirectoryLike)
			{
				await AddTreeAsync(fileSystem, root, items, cancellationToken);
			}
			else if (root.Kind is RemoteEntryKind.File or RemoteEntryKind.SymbolicLink)
			{
				// A link the user picked is read even when the server could not tell what it points at.
				items.Add(new ZipItem(EntryName(root.Name), root, IsDirectory: false));
			}
		}

		return items;
	}

	/// <summary>Bytes the archive will read, for progress. Links count as zero because their listed size is the link's own.</summary>
	public static long TotalBytes(IReadOnlyList<ZipItem> items) =>
		items.Sum(item => !item.IsDirectory && item.Entry.Kind == RemoteEntryKind.File ? item.Entry.Size : 0);

	/// <summary>Writes the archive to <paramref name="destination"/>, reporting cumulative bytes read from the server.</summary>
	public static async Task WriteAsync(
		IRemoteFileSystem fileSystem,
		IReadOnlyList<ZipItem> items,
		Stream destination,
		IProgress<long> progress,
		DateTimeOffset now,
		CancellationToken cancellationToken)
	{
		await using AsyncDrainStream output = new(destination);
		byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
		long transferred = 0;
		try
		{
			// Create mode works on a non-seekable stream: entries are written one after another with data descriptors.
			using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true))
			{
				foreach (ZipItem item in items)
				{
					ZipArchiveEntry entry = archive.CreateEntry(
						item.EntryName,
						item.IsDirectory ? CompressionLevel.NoCompression : CompressionLevel.Fastest);
					entry.LastWriteTime = ToZipTime(item.Entry.LastModified ?? now);
					if (item.IsDirectory)
					{
						continue;
					}

					Stream entryStream = entry.Open();
					await using (entryStream)
					{
						Stream remote = await fileSystem.OpenReadAsync(item.Entry.Path, cancellationToken);
						await using (remote)
						{
							int read;
							while ((read = await remote.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)) > 0)
							{
								await entryStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
								transferred += read;
								progress.Report(transferred);
							}
						}
					}

					await output.DrainAsync(cancellationToken);
				}
			}

			await output.FlushAsync(cancellationToken);
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	private static async Task AddTreeAsync(
		IRemoteFileSystem fileSystem,
		RemoteFileEntry root,
		List<ZipItem> items,
		CancellationToken cancellationToken)
	{
		Stack<(RemoteFileEntry Entry, string Name)> pending = new();
		pending.Push((root, EntryName(root.Name)));
		while (pending.TryPop(out (RemoteFileEntry Entry, string Name) directory))
		{
			cancellationToken.ThrowIfCancellationRequested();
			items.Add(new ZipItem(directory.Name + "/", directory.Entry, IsDirectory: true));

			IReadOnlyList<RemoteFileEntry> children = await fileSystem.ListAsync(directory.Entry.Path, cancellationToken);
			foreach (RemoteFileEntry child in children)
			{
				string name = directory.Name + "/" + EntryName(child.Name);
				if (child.Kind == RemoteEntryKind.Directory)
				{
					pending.Push((child, name));
				}
				else if (child.Kind == RemoteEntryKind.File || child.LinkTargetKind == RemoteEntryKind.File)
				{
					items.Add(new ZipItem(name, child, IsDirectory: false));
				}
			}
		}
	}

	// A '\' is an ordinary character in a POSIX name, but extractors on Windows read it as a folder separator, so a name
	// the server chose, such as "..\..\Startup\x.bat", would climb out of the folder the archive is extracted to.
	private static string EntryName(string remoteName) => remoteName.Replace('\\', '_');

	// Zip stores local DOS times, which only cover 1980 to 2107.
	private static DateTimeOffset ToZipTime(DateTimeOffset value)
	{
		DateTimeOffset local = value.ToLocalTime();
		return local.Year switch
		{
			< 1980 => new DateTimeOffset(1980, 1, 1, 0, 0, 0, local.Offset),
			> 2107 => new DateTimeOffset(2107, 12, 31, 23, 59, 58, local.Offset),
			_ => local,
		};
	}
}
