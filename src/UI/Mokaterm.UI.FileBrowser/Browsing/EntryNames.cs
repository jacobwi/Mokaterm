using System.Globalization;
using System.Text.RegularExpressions;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.UI.FileBrowser.Browsing;

/// <summary>
/// Names for new entries that must not take one already in the folder: "untitled 2.txt", "report copy.txt". A number or
/// a word goes before a file's extension, so the copy still opens with the same program, and at the end of a folder's
/// name, where a dot is just part of the name.
/// </summary>
internal static partial class EntryNames
{
	/// <summary>What a copy adds when its name is taken: <c>report copy.txt</c>, then <c>report copy 2.txt</c>.</summary>
	public const string CopySuffix = " copy";

	/// <summary><paramref name="name"/> itself when it is free, else the first of <c>name 2</c>, <c>name 3</c> and on that is.</summary>
	public static string Unique(string name, IReadOnlySet<string> taken, bool isFolder)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(taken);
		if (!taken.Contains(name))
		{
			return name;
		}

		(string stem, string extension) = Split(name, isFolder);
		for (int number = 2; ; number++)
		{
			string candidate = string.Create(CultureInfo.InvariantCulture, $"{stem} {number}{extension}");
			if (!taken.Contains(candidate))
			{
				return candidate;
			}
		}
	}

	/// <summary>
	/// The name a copy of <paramref name="name"/> takes beside entries named <paramref name="taken"/>: <c>report copy.txt</c>,
	/// <c>report copy 2.txt</c>. A copy of a copy counts on from it rather than growing a second "copy".
	/// </summary>
	public static string CopyName(string name, IReadOnlySet<string> taken, bool isFolder)
	{
		ArgumentNullException.ThrowIfNull(name);
		(string stem, string extension) = Split(name, isFolder);
		if (CopiedStem().Match(stem) is { Success: true } copied)
		{
			stem = copied.Groups[1].Value;
		}

		return Unique(stem + CopySuffix + extension, taken, isFolder);
	}

	/// <summary>
	/// The part a suffix goes after and the extension it goes before. A leading dot names a hidden file rather than starting
	/// an extension, and an archive keeps its whole <c>.tar.gz</c> together.
	/// </summary>
	internal static (string Stem, string Extension) Split(string name, bool isFolder)
	{
		if (isFolder)
		{
			return (name, "");
		}

		if (RemoteArchives.ExtensionOf(name) is { } archive)
		{
			return (name[..^archive.Length], archive);
		}

		int dot = name.LastIndexOf('.');
		return dot > 0 && dot < name.Length - 1 ? (name[..dot], name[dot..]) : (name, "");
	}

	[GeneratedRegex(@"^(.+) copy(?: [0-9]+)?$", RegexOptions.CultureInvariant)]
	private static partial Regex CopiedStem();
}
