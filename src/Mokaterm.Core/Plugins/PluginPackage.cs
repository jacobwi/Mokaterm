using System.IO.Compression;
using System.Security.Cryptography;
using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.Core.Plugins;

/// <summary>
/// A <c>.mokaplugin</c> read into memory and checked. The bytes that were hashed are the bytes that get written, so
/// what the user agreed to in the install dialog is exactly what lands on disk.
/// </summary>
internal sealed class PluginPackage : IPluginPackage
{
	/// <summary>The largest package file accepted.</summary>
	public const int MaxPackageBytes = 64 * 1024 * 1024;

	private const long MaxFileBytes = 64L * 1024 * 1024;
	private const long MaxTotalBytes = 128L * 1024 * 1024;
	private const int MaxEntries = 4096;
	private const int MaxPathLength = 240;

	private readonly Dictionary<string, byte[]> _contents;
	private bool _disposed;

	private PluginPackage(PluginManifest manifest, Dictionary<string, byte[]> contents, List<PluginPackageFile> files, string? installedVersion, string? problem)
	{
		Manifest = manifest;
		_contents = contents;
		Files = files;
		Hash = PluginHash.Combine(files);
		InstalledVersion = installedVersion;
		Problem = problem;
		ThemeCount = files.Count(file => file.Path.StartsWith(PluginLayout.ThemesFolder + "/", StringComparison.Ordinal));
	}

	public PluginManifest Manifest { get; }

	public string Id => Manifest.Id;

	public string Name => Manifest.Name;

	public string Version => Manifest.Version;

	public string? Publisher => Manifest.Publisher;

	public string? Description => Manifest.Description;

	public bool HasCode => Manifest.HasCode;

	public IReadOnlyList<string> Hosts => Manifest.Hosts;

	public int ThemeCount { get; }

	public IReadOnlyList<PluginPackageFile> Files { get; }

	public string Hash { get; }

	public string? InstalledVersion { get; }

	public string? Problem { get; }

	/// <summary>Reads and checks a package.</summary>
	/// <param name="installedVersion">The version installed under an id, or null when none is.</param>
	/// <param name="hostName">This host's name in <see cref="PluginHosts"/>.</param>
	/// <exception cref="InvalidDataException">The data is not a usable package; the message is for the user.</exception>
	public static PluginPackage Read(byte[] data, Func<string, string?> installedVersion, string hostName)
	{
		ArgumentNullException.ThrowIfNull(data);
		ArgumentNullException.ThrowIfNull(installedVersion);
		if (data.Length > MaxPackageBytes)
		{
			throw new InvalidDataException($"The file is larger than {MaxPackageBytes / (1024 * 1024)} MB, which no plugin should be.");
		}

		Dictionary<string, byte[]> contents = new(StringComparer.OrdinalIgnoreCase);
		List<PluginPackageFile> files = [];
		using ZipArchive zip = OpenZip(data);
		if (zip.Entries.Count > MaxEntries)
		{
			throw new InvalidDataException($"The package holds more than {MaxEntries} files.");
		}

		long total = 0;
		foreach (ZipArchiveEntry entry in zip.Entries)
		{
			// Folders are implied by the files in them.
			if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
			{
				continue;
			}

			string path = CheckPath(entry.FullName);
			if (contents.ContainsKey(path))
			{
				throw new InvalidDataException($"The package holds \"{path}\" twice.");
			}

			byte[] bytes = ReadEntry(entry, MaxTotalBytes - total);
			total += bytes.Length;
			contents.Add(path, bytes);
			files.Add(new PluginPackageFile(path, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes))));
		}

		if (!contents.TryGetValue(PluginLayout.ManifestFile, out byte[]? manifestBytes))
		{
			throw new InvalidDataException("The package has no plugin.json, so it is not a Mokaterm plugin.");
		}

		PluginManifest manifest = PluginManifestReader.Read(manifestBytes);
		bool hasLib = files.Exists(file => file.Path.StartsWith(PluginLayout.LibFolder + "/", StringComparison.Ordinal));
		if (manifest.Assembly is { } assembly && !contents.ContainsKey($"{PluginLayout.LibFolder}/{assembly}"))
		{
			throw new InvalidDataException($"The plugin names {assembly}, which is not in the package's lib folder.");
		}

		if (!manifest.HasCode && hasLib)
		{
			throw new InvalidDataException("The plugin says it carries no code, but its package has a lib folder.");
		}

		files.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
		return new PluginPackage(manifest, contents, files, installedVersion(manifest.Id), PluginCompatibility.Problem(manifest, hostName));
	}

	/// <summary>Writes every file under <paramref name="folder"/>, which must not exist yet.</summary>
	public void ExtractTo(string folder)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		string root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
		Directory.CreateDirectory(root);
		foreach ((string path, byte[] bytes) in _contents)
		{
			string target = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));

			// CheckPath already refused anything that climbs out; this is the second lock on the same door.
			if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException($"The package path \"{path}\" leaves the plugin's folder.");
			}

			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.WriteAllBytes(target, bytes);
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		foreach (byte[] bytes in _contents.Values)
		{
			Array.Clear(bytes);
		}

		_contents.Clear();
	}

	private static string CheckPath(string name)
	{
		string path = name.Replace('\\', '/');
		string[] segments = path.Split('/');
		if (path.Length is 0 or > MaxPathLength
			|| path[0] == '/'
			|| path.Contains(':', StringComparison.Ordinal)
			|| path.Any(char.IsControl)
			|| segments.Any(segment => segment is "" or "." or ".."))
		{
			throw new InvalidDataException($"The package holds a path that is not allowed: \"{Shorten(name)}\".");
		}

		bool topLevelFile = segments.Length == 1;
		if (topLevelFile ? path != PluginLayout.ManifestFile : !PluginLayout.TopLevel.Contains(segments[0], StringComparer.Ordinal))
		{
			throw new InvalidDataException(
				$"The package holds \"{Shorten(path)}\". A plugin package has plugin.json, lib, wwwroot and themes, and nothing else.");
		}

		return path;
	}

	private static ZipArchive OpenZip(byte[] data)
	{
		try
		{
			return new ZipArchive(new MemoryStream(data, writable: false), ZipArchiveMode.Read);
		}
		catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException or ArgumentException)
		{
			throw new InvalidDataException("The file is not a plugin package (it is not a readable zip).", ex);
		}
	}

	// The sizes in a zip's directory are whatever its author wrote, so bytes are counted as they come out.
	private static byte[] ReadEntry(ZipArchiveEntry entry, long remaining)
	{
		long limit = Math.Min(MaxFileBytes, remaining);
		using Stream source = OpenEntry(entry);
		using MemoryStream copy = new();
		byte[] buffer = new byte[81920];
		int read;
		while ((read = ReadSome(source, buffer, entry)) > 0)
		{
			if (copy.Length + read > limit)
			{
				throw new InvalidDataException(limit == MaxFileBytes
					? $"\"{Shorten(entry.FullName)}\" is larger than {MaxFileBytes / (1024 * 1024)} MB unpacked."
					: $"The package unpacks to more than {MaxTotalBytes / (1024 * 1024)} MB.");
			}

			copy.Write(buffer, 0, read);
		}

		return copy.ToArray();
	}

	private static Stream OpenEntry(ZipArchiveEntry entry)
	{
		try
		{
			return entry.Open();
		}
		catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
		{
			throw Damaged(entry, ex);
		}
	}

	private static int ReadSome(Stream source, byte[] buffer, ZipArchiveEntry entry)
	{
		try
		{
			return source.Read(buffer, 0, buffer.Length);
		}
		catch (Exception ex) when (ex is InvalidDataException or IOException)
		{
			throw Damaged(entry, ex);
		}
	}

	private static InvalidDataException Damaged(ZipArchiveEntry entry, Exception inner) =>
		new($"The package is damaged: \"{Shorten(entry.FullName)}\" could not be unpacked.", inner);

	private static string Shorten(string value) => value.Length <= 60 ? value : value[..60] + "...";
}
