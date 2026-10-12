#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Mokaterm.Plugin.Sdk;

/// <summary>
/// Zips a staged plugin folder into a <c>.mokaplugin</c>. Entries are sorted, named with forward slashes (as the zip format
/// asks) and stamped with one fixed time, so a rebuild that changed nothing makes the same bytes. Then the file
/// is left alone, which keeps its time stamp for whatever copies it on.
/// </summary>
public sealed class MokatermPluginZip : Task
{
	// Zip times are local DOS times from 1980 on; any fixed value inside that range does.
	private static readonly DateTimeOffset EntryTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

	[Required]
	public string SourceDirectory { get; set; } = "";

	[Required]
	public string PackagePath { get; set; } = "";

	public override bool Execute()
	{
		string root = Path.GetFullPath(SourceDirectory);
		if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
		{
			root += Path.DirectorySeparatorChar;
		}

		List<KeyValuePair<string, string>> files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
			.Select(file => new KeyValuePair<string, string>(file.Substring(root.Length).Replace('\\', '/'), file))
			.OrderBy(file => file.Key, StringComparer.Ordinal)
			.ToList();

		// The app keys a package's files without regard to case, so two names that differ only in case would collide there.
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, string> file in files)
		{
			if (!seen.Add(file.Key))
			{
				Log.LogError(null, "MOKP006", null, null, 0, 0, 0, 0,
					"The plugin package would hold \"{0}\" twice (names are compared without case).", file.Key);
				return false;
			}
		}

		byte[] package;
		using (MemoryStream buffer = new MemoryStream())
		{
			using (ZipArchive zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
			{
				foreach (KeyValuePair<string, string> file in files)
				{
					ZipArchiveEntry entry = zip.CreateEntry(file.Key, CompressionLevel.Optimal);
					entry.LastWriteTime = EntryTime;
					using (Stream target = entry.Open())
					using (FileStream source = File.OpenRead(file.Value))
					{
						source.CopyTo(target);
					}
				}
			}

			package = buffer.ToArray();
		}

		string output = Path.GetFullPath(PackagePath);
		if (File.Exists(output) && File.ReadAllBytes(output).SequenceEqual(package))
		{
			Log.LogMessage(MessageImportance.Low, "{0} is up to date.", output);
			return true;
		}

		string? folder = Path.GetDirectoryName(output);
		if (!string.IsNullOrEmpty(folder))
		{
			Directory.CreateDirectory(folder);
		}

		File.WriteAllBytes(output, package);
		return !Log.HasLoggedErrors;
	}
}
