using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.DevHost.Demo.FileSystem;

namespace Mokaterm.DevHost.Demo.Docker;

// The manager's volume backup and restore scripts, played over a few seconds the way the real ones print: a backup
// writes a real tar (or tar.gz) of the volume's folder in the demo tree, owned by the login, and a restore unpacks
// one back into a volume's folder. Their helper container shows in the list while they run and goes when they end,
// or when the manager removes it after a stop.
internal sealed partial class DemoDockerEngine
{
	private const string BackupScriptStart = "helper=$1 volume=$2 image=$3 out=$4";
	private const string RestoreScriptStart = "helper=$1 volume=$2 image=$3 archive=$4";

	private readonly Dictionary<string, string> _partialFiles = new(StringComparer.Ordinal);

	private static bool IsBackupScript(string script) => script.StartsWith(BackupScriptStart, StringComparison.Ordinal);

	private static bool IsRestoreScript(string script) => script.StartsWith(RestoreScriptStart, StringComparison.Ordinal);

	private static string VolumeFolder(string volume) => $"/var/lib/docker/volumes/{volume}/_data";

	private static DemoDockerStream Failing(int exit, string message) =>
		new([.. message.Split('\n').Select(line => new DemoDockerLine(Quick, line, true))], () => exit);

	private DemoDockerStream VolumeBackup(string[] arguments)
	{
		if (arguments is not [var helper, var volume, var image, var target, var part, var gzip, _] || _tree is not { } tree)
		{
			return Failing(2, "sh: 1: the demo has no files to write a backup into");
		}

		if (tree.Stat(target, _login) is not null)
		{
			return Failing(3, $"{target} exists already, and a backup never replaces a file.");
		}

		if (ImageById(image) is not { } reference)
		{
			return Failing(125, $"Unable to find image '{image}:latest' locally\ndocker: Error response from daemon: pull access denied for {image}, repository does not exist or may require 'docker login'.");
		}

		DemoNode file;
		try
		{
			file = tree.BeginWrite(part, overwrite: false, UnixFileMode.UserRead | UnixFileMode.UserWrite, _login);
		}
		catch (RemoteFileSystemException ex)
		{
			return Failing(2, $"sh: 1: cannot create {part}: {ex.Message}");
		}

		if (!_volumes.Contains(volume))
		{
			// docker create --mount makes a named volume that is not there yet, empty.
			_volumes.Add(volume);
		}

		byte[] archive = ArchiveOf(tree, volume, gzip == "1");
		AddHelper(helper, reference, volume, "volume-backup");
		_partialFiles[helper] = part;
		string size = DockerSize(archive.Length);
		List<DemoDockerLine> lines =
		[
			new(Quick, $"Copying {volume} out of {helper}, a container that is never started.", false),
			new(TimeSpan.FromSeconds(1.5), $"Written so far: {DockerSize(archive.Length / 3)}", false),
			new(TimeSpan.FromSeconds(1.5), $"Written so far: {DockerSize(archive.Length * 2 / 3)}", false),
			new(TimeSpan.FromSeconds(1), $"Wrote {target}, {size}.", false),
		];

		return new DemoDockerStream(lines, () =>
		{
			lock (_lock)
			{
				try
				{
					tree.Append(file, archive);
					tree.CompleteWrite(file, null);
					tree.Rename(part, target, overwrite: false, _login);
				}
				catch (RemoteFileSystemException)
				{
					return 1;
				}
				finally
				{
					RemoveHelper(helper);
				}

				return 0;
			}
		});
	}

	private DemoDockerStream VolumeRestore(string[] arguments)
	{
		if (arguments is not [var helper, var volume, var image, var archive, var create, _] || _tree is not { } tree)
		{
			return Failing(2, "sh: 1: the demo has no files to restore from");
		}

		DemoFileData data;
		try
		{
			data = tree.Read(archive, _login);
		}
		catch (RemoteFileSystemException)
		{
			return Failing(2, $"{archive} is not a file this account can read.");
		}

		if (create == "1" && _volumes.Contains(volume))
		{
			return Failing(3, $"A volume named {volume} exists already.");
		}

		if (ImageById(image) is not { } reference)
		{
			return Failing(125, $"Unable to find image '{image}:latest' locally");
		}

		if (!data.IsComplete)
		{
			return Failing(1, "Error response from daemon: Error processing tar file(exit status 1): unexpected EOF");
		}

		List<(string Path, TarEntryType Type, UnixFileMode Mode, byte[] Content)> entries;
		try
		{
			entries = Unpacked(data.Content);
		}
		catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or EndOfStreamException)
		{
			return Failing(1, "Error response from daemon: Error processing tar file(exit status 1): " + ex.Message);
		}

		AddHelper(helper, reference, volume, "volume-restore");
		List<DemoDockerLine> lines =
		[
			new(Quick, $"Unpacking {archive} into {volume} through {helper}, a container that is never started.", false),
			new(TimeSpan.FromSeconds(1.5), "Read 40% of the archive", false),
			new(TimeSpan.FromSeconds(1.5), "Read 90% of the archive", false),
			new(TimeSpan.FromSeconds(0.8), $"Restored {archive} into {volume}.", false),
		];

		return new DemoDockerStream(lines, () =>
		{
			lock (_lock)
			{
				try
				{
					if (!_volumes.Contains(volume))
					{
						_volumes.Add(volume);
					}

					string folder = VolumeFolder(volume);
					EnsureFolder(tree, folder);
					foreach ((string path, TarEntryType type, UnixFileMode mode, byte[] content) in entries)
					{
						string at = path.Length == 0 ? folder : RemotePath.Combine(folder, path);
						if (type == TarEntryType.Directory)
						{
							EnsureFolder(tree, at);
							continue;
						}

						EnsureFolder(tree, RemotePath.GetParent(at));
						DemoNode written = tree.BeginWrite(at, overwrite: true, mode, DemoAccount.Root);
						tree.Append(written, content);
						tree.CompleteWrite(written, null);
					}
				}
				catch (RemoteFileSystemException)
				{
					return 1;
				}
				finally
				{
					RemoveHelper(helper);
				}

				return 0;
			}
		});
	}

	// The tar docker cp - writes for /mokaterm-volume/.: the folder itself as "./", then everything under it.
	private static byte[] ArchiveOf(DemoFileTree tree, string volume, bool gzip)
	{
		using MemoryStream buffer = new();
		if (gzip)
		{
			using GZipStream zipped = new(buffer, CompressionLevel.Optimal, leaveOpen: true);
			WriteArchive(tree, zipped, volume);
		}
		else
		{
			WriteArchive(tree, buffer, volume);
		}

		return buffer.ToArray();
	}

	private static void WriteArchive(DemoFileTree tree, Stream sink, string volume)
	{
		using TarWriter writer = new(sink, TarEntryFormat.Ustar, leaveOpen: true);
		string folder = VolumeFolder(volume);
		RemoteFileEntry? root = tree.Stat(folder, DemoAccount.Root);
		writer.WriteEntry(Entry(TarEntryType.Directory, "./", root));
		if (root is { Kind: RemoteEntryKind.Directory })
		{
			Add(tree, writer, folder, "./");
		}
	}

	private static void Add(DemoFileTree tree, TarWriter writer, string folder, string prefix)
	{
		foreach (RemoteFileEntry entry in tree.ListDirectory(folder, DemoAccount.Root).OrderBy(entry => entry.Name, StringComparer.Ordinal))
		{
			string name = prefix + entry.Name;
			if (entry.Kind == RemoteEntryKind.Directory)
			{
				writer.WriteEntry(Entry(TarEntryType.Directory, name + "/", entry));
				Add(tree, writer, entry.Path, name + "/");
			}
			else if (entry.Kind == RemoteEntryKind.File)
			{
				UstarTarEntry file = Entry(TarEntryType.RegularFile, name, entry);
				file.DataStream = new MemoryStream(tree.Read(entry.Path, DemoAccount.Root).Content);
				writer.WriteEntry(file);
			}
		}
	}

	private static UstarTarEntry Entry(TarEntryType type, string name, RemoteFileEntry? entry)
	{
		int id = int.TryParse(entry?.Owner, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : 0;
		return new UstarTarEntry(type, name)
		{
			Mode = entry?.Permissions ?? (type == TarEntryType.Directory ? DemoModes.NewDirectory : DemoModes.NewFile),
			Uid = id,
			Gid = id,
			UserName = entry?.Owner ?? "root",
			GroupName = entry?.Group ?? "root",
			ModificationTime = entry?.LastModified ?? DateTimeOffset.UnixEpoch,
		};
	}

	// Plain tar or gzip; docker reads bzip2 and xz too, which the demo does not.
	private static List<(string Path, TarEntryType Type, UnixFileMode Mode, byte[] Content)> Unpacked(byte[] archive)
	{
		bool gzip = archive.Length > 2 && archive[0] == 0x1f && archive[1] == 0x8b;
		bool other = (archive.Length > 3 && archive[0] == 'B' && archive[1] == 'Z' && archive[2] == 'h')
			|| (archive.Length > 5 && archive[0] == 0xfd && archive[1] == '7' && archive[2] == 'z');
		if (other)
		{
			throw new NotSupportedException("the demo unpacks tar and tar.gz only");
		}

		using MemoryStream source = new(archive);
		using Stream plain = gzip ? new GZipStream(source, CompressionMode.Decompress) : source;
		using TarReader reader = new(plain);
		List<(string, TarEntryType, UnixFileMode, byte[])> entries = [];
		while (reader.GetNextEntry(copyData: true) is { } entry)
		{
			string path = entry.Name.Replace('\\', '/');
			path = path.StartsWith("./", StringComparison.Ordinal) ? path[2..] : path == "." ? "" : path;
			path = path.TrimEnd('/');
			if (path.StartsWith('/') || path.Split('/').Contains(".."))
			{
				throw new InvalidDataException($"{entry.Name} would land outside the volume");
			}

			using MemoryStream content = new();
			entry.DataStream?.CopyTo(content);
			TarEntryType type = entry.EntryType == TarEntryType.Directory ? TarEntryType.Directory : TarEntryType.RegularFile;
			if (entry.EntryType is TarEntryType.Directory or TarEntryType.RegularFile or TarEntryType.V7RegularFile)
			{
				entries.Add((path, type, entry.Mode, content.ToArray()));
			}
		}

		return entries;
	}

	private static void EnsureFolder(DemoFileTree tree, string folder)
	{
		if (RemotePath.IsRoot(folder) || tree.Stat(folder, DemoAccount.Root) is not null)
		{
			return;
		}

		EnsureFolder(tree, RemotePath.GetParent(folder));
		tree.CreateDirectory(folder, DemoAccount.Root);
	}

	private string? ImageById(string id) =>
		_images.FirstOrDefault(image => image.Id == "sha256:" + id || image.Id == id) is { Id.Length: > 0 } found
			? found.Repository == "<none>" ? found.Id[7..19] : $"{found.Repository}:{found.Tag}"
			: null;

	private void AddHelper(string helper, string image, string volume, string purpose)
	{
		DateTimeOffset now = _time.GetUtcNow();
		_containers.Add(new DemoDockerContainer
		{
			Id = HexId(helper),
			Name = helper,
			Image = image,
			Command = "/mokaterm-never-runs",
			Created = now,
			State = "created",
			StartedAt = now,
			Labels = new Dictionary<string, string> { ["com.mokaterm.helper"] = purpose },
			Volumes = [volume],
			LogLines = [],
		});
	}

	// What the scripts' trap does: the helper goes, and with it a backup's partial file.
	private void RemoveHelper(string helper)
	{
		_containers.RemoveAll(container => container.Name == helper);
		if (_partialFiles.Remove(helper, out string? part) && _tree is { } tree)
		{
			try
			{
				if (tree.Stat(part, _login) is not null)
				{
					tree.Delete(part, recursive: false, _login);
				}
			}
			catch (RemoteFileSystemException)
			{
				// Already moved into place, or gone.
			}
		}
	}
}
