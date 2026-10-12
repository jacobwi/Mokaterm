using System.Security.Cryptography;
using System.Text;
using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.Core.Plugins;

/// <summary>
/// The value a user trusts when turning a code plugin on: SHA-256 over every file's path and SHA-256, sorted by path.
/// A package and the folder it was installed into give the same value, so trusting a package in the install dialog
/// and checking the folder at every start compare like with like.
/// </summary>
internal static class PluginHash
{
	public static string Combine(IEnumerable<PluginPackageFile> files)
	{
		ArgumentNullException.ThrowIfNull(files);
		using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		foreach (PluginPackageFile file in files.OrderBy(file => file.Path, StringComparer.Ordinal))
		{
			hash.AppendData(Encoding.UTF8.GetBytes($"{file.Path}\n{file.Sha256}\n"));
		}

		return Convert.ToHexStringLower(hash.GetHashAndReset());
	}

	/// <summary>Every file under <paramref name="folder"/>, as a package would list it.</summary>
	public static List<PluginPackageFile> ListFolder(string folder)
	{
		List<PluginPackageFile> files = [];
		foreach (string path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
		{
			using FileStream stream = File.OpenRead(path);
			string relative = Path.GetRelativePath(folder, path).Replace(Path.DirectorySeparatorChar, '/');
			files.Add(new PluginPackageFile(relative, stream.Length, Convert.ToHexStringLower(SHA256.HashData(stream))));
		}

		return files;
	}

	public static string HashFolder(string folder) => Combine(ListFolder(folder));
}
