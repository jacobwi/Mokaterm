using System.Globalization;
using Moka.Red.Core.Enums;
using Moka.Red.Core.Icons;
using Moka.Red.Icons;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.Common.Formatting;
using Mokaterm.UI.Common.Icons;

namespace Mokaterm.UI.FileBrowser.Browsing;

/// <summary>How entries look in the list and in dialogs: icon, accent, kind and the ls-style columns.</summary>
internal static class EntryVisuals
{
	public static MokaIconDefinition IconFor(RemoteFileEntry entry) => entry.Kind switch
	{
		RemoteEntryKind.Directory => MokatermIcons.Folder,
		RemoteEntryKind.SymbolicLink when entry.LinkTargetKind == RemoteEntryKind.Directory => MokatermIcons.FolderOpen,
		RemoteEntryKind.SymbolicLink => MokatermIcons.Symlink,
		RemoteEntryKind.Other => MokaIcons.Status.HelpCircle,
		_ => MokatermIcons.File,
	};

	public static MokaColor? ColorFor(RemoteFileEntry entry) => entry.Kind switch
	{
		RemoteEntryKind.Directory => MokaColor.Primary,
		RemoteEntryKind.SymbolicLink => MokaColor.Info,
		RemoteEntryKind.Other => MokaColor.Warning,
		_ => null,
	};

	public static string KindLabel(RemoteFileEntry entry) => entry.Kind switch
	{
		RemoteEntryKind.Directory => "Folder",
		RemoteEntryKind.File => "File",
		RemoteEntryKind.SymbolicLink => entry.LinkTargetKind switch
		{
			RemoteEntryKind.Directory => "Link to a folder",
			RemoteEntryKind.File => "Link to a file",
			_ => "Link",
		},
		_ => "Special file",
	};

	/// <summary><c>2 folders, 3 files, 1 link</c> for a selection of several entries. Devices and the like count as files.</summary>
	public static string KindSummary(IReadOnlyList<RemoteFileEntry> entries)
	{
		int folders = entries.Count(entry => entry.Kind == RemoteEntryKind.Directory);
		int links = entries.Count(entry => entry.Kind == RemoteEntryKind.SymbolicLink);
		return CountList(entries.Count - folders - links, folders, links);
	}

	/// <summary><c>1,204 files, 38 folders, 2 links</c>, leaving out the kinds with none. Empty when all are zero.</summary>
	public static string CountList(long files, long folders, long links)
	{
		List<string> parts = [];
		if (folders > 0)
		{
			parts.Add(Count(folders, "folder", "folders"));
		}

		if (files > 0)
		{
			parts.Add(Count(files, "file", "files"));
		}

		if (links > 0)
		{
			parts.Add(Count(links, "link", "links"));
		}

		return string.Join(", ", parts);
	}

	/// <summary><c>1 file</c> or <c>1,204 files</c>.</summary>
	public static string Count(long count, string singular, string plural) => count == 1
		? "1 " + singular
		: string.Create(CultureInfo.CurrentCulture, $"{count:N0} {plural}");

	/// <summary><c>512 B</c>, or <c>12.3 MB (12,912,331 bytes)</c> once the short form rounds.</summary>
	public static string DetailedSize(long bytes) => bytes < 1024
		? DisplayFormat.Bytes(bytes)
		: string.Create(CultureInfo.CurrentCulture, $"{DisplayFormat.Bytes(bytes)} ({bytes:N0} bytes)");

	/// <summary>What a link points at, as the listing reported it.</summary>
	public static string LinkTargetKindLabel(RemoteFileEntry link) => link.LinkTargetKind switch
	{
		RemoteEntryKind.Directory => "folder",
		RemoteEntryKind.File => "file",
		RemoteEntryKind.Other => "special file",
		_ => "broken link",
	};

	/// <summary>Blank for anything but regular files: a link's listed size is the link's own.</summary>
	public static string SizeText(RemoteFileEntry entry) =>
		entry.Kind == RemoteEntryKind.File ? DisplayFormat.Bytes(entry.Size) : "";

	/// <summary><c>drwxr-xr-x</c> like <c>ls -l</c>, or blank when the server reports no mode.</summary>
	public static string PermissionsText(RemoteFileEntry entry) => entry.Permissions is { } mode
		? TypeCharacter(entry) + UnixFileModeFormat.ToSymbolic(mode)
		: "";

	private static string TypeCharacter(RemoteFileEntry entry) => entry.Kind switch
	{
		RemoteEntryKind.Directory => "d",
		RemoteEntryKind.SymbolicLink => "l",
		RemoteEntryKind.File => "-",
		_ => "?",
	};
}
