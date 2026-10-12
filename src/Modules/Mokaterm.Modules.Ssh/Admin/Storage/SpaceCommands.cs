using System.Globalization;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Admin.Storage;

/// <summary>
/// What is using space under a folder (<c>du -x -k -d 1</c>, flags BusyBox has too) and which files there are large
/// (<c>find -xdev -size +Nk</c>). Both stay on the folder's own file system and both are stopped on the server after
/// <see cref="ServerSeconds"/>, so a huge tree costs a bounded wait and leaves no du running behind.
/// </summary>
internal static class SpaceCommands
{
	/// <summary>How long du or find may run on the server.</summary>
	public const int ServerSeconds = 50;

	/// <summary>The most large files listed; the search stops there.</summary>
	public const int MaxLargeFiles = 500;

	/// <summary>The size a large file is over, in KiB: 10 MB, 100 MB, 1 GB.</summary>
	public static readonly IReadOnlyList<long> LargeFileSizesKb = [10 * 1024, 100 * 1024, 1024 * 1024];

	/// <summary>The script's own limit, past the server's, for a server that has no <c>timeout</c> at all.</summary>
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(ServerSeconds + 25);

	private const int OutputBytes = 1024 * 1024;

	private static readonly string[] Limit = [ShellTimeout.Function];

	/// <summary>
	/// The size of <paramref name="path"/> and of every folder directly in it. Folders on another file system (a mount
	/// under it) are left out by <c>-x</c>. Errors go to stdout with the sizes, so unreadable folders can be counted.
	/// </summary>
	public static AdminScript Measure(string path, bool asRoot)
	{
		if (AdminNames.CheckFolderPath(path) is { } problem)
		{
			throw new ArgumentException(problem, nameof(path));
		}

		return new AdminScript
		{
			Name = "disks.usage",
			AsRoot = asRoot,
			Timeout = Timeout,
			OutputLimitBytes = OutputBytes,
			Body = string.Join('\n', [
				.. Limit,
				$"mt_path={PosixShell.Quote(path)}",
				AdminSections.Start("du"),
				$"mokaterm_limit {ServerSeconds} du -x -k -d 1 -- \"$mt_path\" 2>&1",
				"echo \"@@mt:exit $?\""]),
		};
	}

	/// <summary>
	/// Regular files over <paramref name="sizeKb"/> under <paramref name="path"/> on its own file system, at most
	/// <see cref="MaxLargeFiles"/>, each with its size from <c>ls -ln</c> (find's <c>-printf</c> is GNU only). The exit
	/// line comes last, so a list that <c>head</c> cut short has none.
	/// </summary>
	public static AdminScript LargeFiles(string path, long sizeKb, bool asRoot)
	{
		if (AdminNames.CheckFolderPath(path) is { } problem)
		{
			throw new ArgumentException(problem, nameof(path));
		}

		ArgumentOutOfRangeException.ThrowIfLessThan(sizeKb, 1);
		string size = sizeKb.ToString(CultureInfo.InvariantCulture);
		string lines = (MaxLargeFiles + 1).ToString(CultureInfo.InvariantCulture);
		return new AdminScript
		{
			Name = "disks.large-files",
			AsRoot = asRoot,
			Timeout = Timeout,
			OutputLimitBytes = OutputBytes,
			Body = string.Join('\n', [
				.. Limit,
				"set -f",
				$"mt_path={PosixShell.Quote(path)}",
				AdminSections.Start("files"),
				$"{{ mokaterm_limit {ServerSeconds} find \"$mt_path\" -xdev -type f -size +{size}k 2>/dev/null; echo \"@@mt:exit $?\"; }} | head -n {lines} | while IFS= read -r mt_file; do",
				"\tcase \"$mt_file\" in",
				"\t\t@@mt:*) printf '%s\\n' \"$mt_file\" ;;",
				"\t\t*) set -- $(ls -lnd -- \"$mt_file\" 2>/dev/null); if [ -n \"$5\" ]; then printf '%s\\t%s\\n' \"$5\" \"$mt_file\"; fi ;;",
				"\tesac",
				"done"]),
		};
	}

	/// <summary>
	/// du's lines (<c>12345&lt;tab&gt;/var/log</c>), its complaints and its exit code. The line for
	/// <paramref name="path"/> itself is the total and the others are its folders; du prints <c>//bin</c> for a
	/// child of <c>/</c> on some systems, so paths are compared once their slashes are tidied.
	/// </summary>
	public static SpaceReport ParseMeasure(string output, string path, bool truncated)
	{
		ArgumentNullException.ThrowIfNull(output);
		AdminSections sections = AdminSections.Parse(output);
		string folder = Tidy(path);
		long? total = null;
		List<SpaceEntry> entries = [];
		int unreadable = 0;
		string firstProblem = "";
		foreach (string line in AdminSections.Lines(sections.Get("du")))
		{
			if (TrySize(line, out long kb, out string entryPath))
			{
				if (entryPath == folder)
				{
					total = kb;
				}
				else if (Parent(entryPath) == folder)
				{
					entries.Add(new SpaceEntry(entryPath, kb));
				}

				continue;
			}

			unreadable++;
			if (firstProblem.Length == 0)
			{
				firstProblem = RemoteText.FirstLine(line);
			}
		}

		int? exit = ExitCode(sections);
		return new SpaceReport
		{
			Path = folder,
			TotalKb = total,
			Entries = [.. entries.OrderByDescending(entry => entry.Kb).ThenBy(entry => entry.Path, StringComparer.Ordinal)],
			Unreadable = unreadable,
			FirstProblem = firstProblem,
			StoppedEarly = exit == 124,
			Truncated = truncated,
		};
	}

	/// <summary>The large file search: <c>bytes&lt;tab&gt;path</c> lines, then its exit line unless head cut the list short.</summary>
	public static SpaceReport ParseLargeFiles(string output, string path, bool truncated)
	{
		ArgumentNullException.ThrowIfNull(output);
		AdminSections sections = AdminSections.Parse(output);
		List<SpaceEntry> files = [];
		foreach (string line in AdminSections.Lines(sections.Get("files")))
		{
			if (TrySize(line, out long bytes, out string file))
			{
				files.Add(new SpaceEntry(file, (bytes + 1023) / 1024));
			}
		}

		int? exit = ExitCode(sections);
		return new SpaceReport
		{
			Path = Tidy(path),
			Entries = [.. files.OrderByDescending(file => file.Kb).ThenBy(file => file.Path, StringComparer.Ordinal)],
			StoppedEarly = exit == 124,
			Capped = exit is null && files.Count >= MaxLargeFiles,
			Truncated = truncated,
		};
	}

	/// <summary>The folder above <paramref name="path"/>, or null for <c>/</c>.</summary>
	public static string? Parent(string path)
	{
		string tidy = Tidy(path);
		if (tidy == "/")
		{
			return null;
		}

		int slash = tidy.LastIndexOf('/');
		return slash <= 0 ? "/" : tidy[..slash];
	}

	/// <summary>The last part of a path, or <c>/</c> for the root.</summary>
	public static string NameOf(string path)
	{
		string tidy = Tidy(path);
		return tidy == "/" ? "/" : tidy[(tidy.LastIndexOf('/') + 1)..];
	}

	/// <summary>Each folder from <c>/</c> down to <paramref name="path"/>, for a breadcrumb.</summary>
	public static IReadOnlyList<(string Name, string Path)> Trail(string path)
	{
		List<(string, string)> trail = [("/", "/")];
		string current = "";
		foreach (string part in Tidy(path).Split('/', StringSplitOptions.RemoveEmptyEntries))
		{
			current += "/" + part;
			trail.Add((part, current));
		}

		return trail;
	}

	/// <summary>One slash between parts and none at the end, except for <c>/</c> itself.</summary>
	public static string Tidy(string path)
	{
		ArgumentNullException.ThrowIfNull(path);
		string joined = "/" + string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries));
		return joined;
	}

	private static bool TrySize(string line, out long size, out string path)
	{
		size = 0;
		path = "";
		int tab = line.IndexOf('\t', StringComparison.Ordinal);
		if (tab <= 0 || !long.TryParse(line.AsSpan(0, tab), NumberStyles.None, CultureInfo.InvariantCulture, out size))
		{
			return false;
		}

		string rest = line[(tab + 1)..];
		if (!rest.StartsWith('/'))
		{
			return false;
		}

		path = Tidy(rest);
		return true;
	}

	private static int? ExitCode(AdminSections sections)
	{
		foreach ((string value, _) in sections.All("exit"))
		{
			if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int code))
			{
				return code;
			}
		}

		return null;
	}
}
