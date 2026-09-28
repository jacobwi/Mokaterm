using System.Buffers;
using System.Globalization;
using System.Text;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.DevHost.Demo.FileSystem;

namespace Mokaterm.DevHost.Demo.Terminal;

// File commands: ls, cat, mkdir, touch, rm and tail.
internal sealed partial class DemoShell
{
	private const string BrokenLinkColor = "\u001b[40;31;1m";
	private const string OpenFolderColor = "\u001b[30;42m";
	private const string ImageColor = "\u001b[1;35m";

	private static readonly TimeSpan FollowInterval = TimeSpan.FromMilliseconds(200);

	private static readonly SearchValues<char> NeedsQuoting = SearchValues.Create(" \t'\"()[]{}<>&;|*?$!#`\\");

	private static readonly string[] SizeUnits = ["K", "M", "G", "T"];

	// The dircolors defaults for archives and images.
	private static readonly string[] ArchiveExtensions = [".tar", ".gz", ".tgz", ".zip", ".xz", ".7z", ".deb", ".rpm", ".jar"];

	private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp"];

	private void ListFiles(string[] args, ShellOutput output)
	{
		bool showHidden = false;
		bool showDots = false;
		bool longFormat = false;
		bool human = false;
		List<string> targets = [];
		foreach (string arg in args)
		{
			if (arg.Length < 2 || arg[0] != '-')
			{
				targets.Add(arg);
				continue;
			}

			foreach (char option in arg.AsSpan(1))
			{
				switch (option)
				{
					case 'a':
						showHidden = true;
						showDots = true;
						break;
					case 'A':
						showHidden = true;
						break;
					case 'l':
						longFormat = true;
						break;
					case 'h':
						human = true;
						break;
					default:
						output.WriteLine($"ls: invalid option -- '{option}'");
						return;
				}
			}
		}

		if (targets.Count == 0)
		{
			targets.Add(".");
		}

		// Like GNU ls: files named on the command line first, as one block, then each folder under its name.
		List<RemoteFileEntry> files = [];
		List<(string Target, string Path)> folders = [];
		foreach (string target in targets)
		{
			string path = PathOf(target);
			RemoteFileEntry? entry;
			try
			{
				entry = _tree.Stat(path, _account);
			}
			catch (RemoteFileSystemException ex)
			{
				output.WriteLine($"ls: cannot access '{target}': {ex.Message}");
				continue;
			}

			if (entry is null)
			{
				output.WriteLine($"ls: cannot access '{target}': No such file or directory");
			}
			else if (entry.Kind == RemoteEntryKind.Directory || (entry.LinkTargetKind == RemoteEntryKind.Directory && !longFormat))
			{
				// A link to a folder lists the folder, unless -l asks about the link itself.
				folders.Add((target, path));
			}
			else
			{
				files.Add(entry with { Name = target });
			}
		}

		WriteEntries(files, longFormat, human, withTotal: false, output);
		bool separate = files.Count > 0;
		foreach ((string target, string path) in folders)
		{
			List<RemoteFileEntry> entries;
			try
			{
				entries = _tree.ListDirectory(path, _account);
			}
			catch (RemoteFileSystemException ex)
			{
				output.WriteLine($"ls: cannot open directory '{target}': {ex.Message}");
				continue;
			}

			entries.RemoveAll(item => item.IsHidden && !showHidden);
			entries.Sort(static (a, b) => CompareNames(a.Name, b.Name));
			if (showDots)
			{
				entries.InsertRange(0, DotEntries(path));
			}

			if (targets.Count > 1)
			{
				if (separate)
				{
					output.WriteLine();
				}

				output.WriteLine(target + ":");
				separate = true;
			}

			WriteEntries(entries, longFormat, human, withTotal: true, output);
		}
	}

	private List<RemoteFileEntry> DotEntries(string path)
	{
		string folder = _tree.Resolve(path, _account);
		List<RemoteFileEntry> dots = [];
		if (_tree.Stat(folder, _account) is { } self)
		{
			dots.Add(self with { Name = "." });
		}

		if (_tree.Stat(RemotePath.GetParent(folder), _account) is { } parent)
		{
			dots.Add(parent with { Name = ".." });
		}

		return dots;
	}

	private void WriteEntries(List<RemoteFileEntry> entries, bool longFormat, bool human, bool withTotal, ShellOutput output)
	{
		if (longFormat)
		{
			WriteLong(entries, human, withTotal, output);
		}
		else
		{
			WriteColumns(entries, output);
		}
	}

	private void WriteLong(List<RemoteFileEntry> entries, bool human, bool withTotal, ShellOutput output)
	{
		if (withTotal)
		{
			// ls counts 1K blocks, and files take whole 4K blocks on ext4.
			long blocks = entries.Sum(entry => entry.Kind == RemoteEntryKind.SymbolicLink ? 0 : (entry.Size + 4095) / 4096 * 4);
			output.WriteLine("total " + (human ? HumanSize(blocks * 1024) : blocks.ToString(CultureInfo.InvariantCulture)));
		}

		if (entries.Count == 0)
		{
			return;
		}

		string[] sizes = [.. entries.Select(entry => human ? HumanSize(entry.Size) : entry.Size.ToString(CultureInfo.InvariantCulture))];
		int ownerWidth = entries.Max(entry => (entry.Owner ?? "").Length);
		int groupWidth = entries.Max(entry => (entry.Group ?? "").Length);
		int sizeWidth = sizes.Max(size => size.Length);
		DateTimeOffset now = Now;
		for (int i = 0; i < entries.Count; i++)
		{
			RemoteFileEntry entry = entries[i];
			char type = entry.Kind switch
			{
				RemoteEntryKind.Directory => 'd',
				RemoteEntryKind.SymbolicLink => 'l',
				_ => '-',
			};

			string mode = type + UnixFileModeFormat.ToSymbolic(entry.Permissions ?? UnixFileMode.None);
			string links = entry.Kind == RemoteEntryKind.Directory ? "2" : "1";
			string owner = (entry.Owner ?? "").PadRight(ownerWidth);
			string group = (entry.Group ?? "").PadRight(groupWidth);
			string date = DemoDates.Listing(entry.LastModified ?? now, now);
			string linkTarget = entry.Kind == RemoteEntryKind.SymbolicLink ? " -> " + entry.LinkTarget : "";
			output.WriteLine($"{mode} {links} {owner} {group} {sizes[i].PadLeft(sizeWidth)} {date} {Colored(entry, Quote(entry.Name))}{linkTarget}");
		}
	}

	private void WriteColumns(List<RemoteFileEntry> entries, ShellOutput output)
	{
		if (entries.Count == 0)
		{
			return;
		}

		string[] names = [.. entries.Select(entry => Quote(entry.Name))];
		int[] widths = [.. names.Select(TerminalCells.Width)];
		int available = Math.Max(Size.Columns, 20);

		// Like GNU ls: as many columns as fit, filled top to bottom, each as wide as its longest name plus two spaces.
		int columns = Math.Clamp(available / 3, 1, names.Length);
		int rows;
		int[] columnWidths;
		while (true)
		{
			rows = (names.Length + columns - 1) / columns;
			columnWidths = new int[columns];
			for (int i = 0; i < names.Length; i++)
			{
				columnWidths[i / rows] = Math.Max(columnWidths[i / rows], widths[i]);
			}

			if (columns == 1 || columnWidths.Sum() + (2 * (columns - 1)) <= available)
			{
				break;
			}

			columns--;
		}

		StringBuilder line = new();
		for (int row = 0; row < rows; row++)
		{
			line.Clear();
			for (int column = 0; column < columns; column++)
			{
				int index = (column * rows) + row;
				if (index >= names.Length)
				{
					break;
				}

				line.Append(Colored(entries[index], names[index]));
				bool lastInRow = column == columns - 1 || ((column + 1) * rows) + row >= names.Length;
				if (!lastInRow)
				{
					line.Append(' ', columnWidths[column] - widths[index] + 2);
				}
			}

			output.WriteLine(line.ToString());
		}
	}

	private void Concatenate(string[] args, ShellOutput output)
	{
		foreach (string arg in args)
		{
			DemoFileData file;
			try
			{
				file = _tree.Read(PathOf(arg), _account);
			}
			catch (RemoteFileSystemException ex)
			{
				output.WriteLine($"cat: {arg}: {ex.Message}");
				continue;
			}

			if ((file.Content.Length == 0 && file.Size > 0) || !DemoText.TryDecode(file.Content, out string? text))
			{
				output.WriteLine($"{Ansi.Dim}cat: {arg}: {HumanSize(file.Size)} of binary data, not printed by the demo shell{Ansi.Reset}");
				continue;
			}

			output.Write(DemoText.ToTerminalLines(text));
			if (!file.IsComplete)
			{
				if (!text.EndsWith('\n'))
				{
					output.WriteLine();
				}

				output.WriteLine($"{Ansi.Dim}[the demo stores only the first {HumanSize(file.Content.Length)} of {HumanSize(file.Size)}]{Ansi.Reset}");
			}
		}
	}

	private void MakeDirectories(string[] args, ShellOutput output)
	{
		bool parents = args.Contains("-p");
		string[] targets = [.. args.Where(arg => arg != "-p")];
		if (targets.Length == 0)
		{
			output.WriteLine("mkdir: missing operand");
			return;
		}

		foreach (string target in targets)
		{
			try
			{
				string path = PathOf(target);
				if (!parents)
				{
					_tree.CreateDirectory(path, _account);
					continue;
				}

				foreach (RemotePathSegment segment in RemotePath.GetSegments(path).Skip(1))
				{
					if (_tree.Stat(segment.Path, _account) is not { IsDirectoryLike: true })
					{
						_tree.CreateDirectory(segment.Path, _account);
					}
				}
			}
			catch (RemoteFileSystemException ex)
			{
				output.WriteLine($"mkdir: cannot create directory '{target}': {ex.Message}");
			}
		}
	}

	private void Touch(string[] args, ShellOutput output)
	{
		if (args.Length == 0)
		{
			output.WriteLine("touch: missing file operand");
			return;
		}

		foreach (string target in args)
		{
			try
			{
				_tree.Touch(PathOf(target), _account);
			}
			catch (RemoteFileSystemException ex)
			{
				output.WriteLine($"touch: cannot touch '{target}': {ex.Message}");
			}
		}
	}

	private void Remove(string[] args, ShellOutput output)
	{
		bool recursive = false;
		bool force = false;
		List<string> targets = [];
		foreach (string arg in args)
		{
			if (arg.Length > 1 && arg[0] == '-' && !arg.AsSpan(1).ContainsAnyExcept("rRf"))
			{
				recursive |= arg.AsSpan(1).ContainsAny("rR");
				force |= arg.Contains('f', StringComparison.Ordinal);
			}
			else
			{
				targets.Add(arg);
			}
		}

		if (targets.Count == 0 && !force)
		{
			output.WriteLine("rm: missing operand");
			return;
		}

		foreach (string target in targets)
		{
			string path = PathOf(target);
			try
			{
				RemoteFileEntry? entry = _tree.Stat(path, _account);
				if (entry is null)
				{
					if (!force)
					{
						output.WriteLine($"rm: cannot remove '{target}': No such file or directory");
					}
				}
				else if (entry.Kind == RemoteEntryKind.Directory && !recursive)
				{
					output.WriteLine($"rm: cannot remove '{target}': Is a directory");
				}
				else
				{
					_tree.Delete(path, recursive, _account);
				}
			}
			catch (RemoteFileSystemException ex)
			{
				output.WriteLine($"rm: cannot remove '{target}': {ex.Message}");
			}
		}
	}

	private async Task TailAsync(string[] args, ShellOutput output, CancellationToken cancellationToken)
	{
		bool follow = false;
		int count = 10;
		string? target = null;
		for (int i = 0; i < args.Length; i++)
		{
			string arg = args[i];
			if (arg is "-f" or "-F" or "--follow")
			{
				follow = true;
			}
			else if (arg == "-n" && i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int separate))
			{
				count = separate;
				i++;
			}
			else if (arg.Length > 2 && arg.StartsWith("-n", StringComparison.Ordinal) && int.TryParse(arg.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out int attached))
			{
				count = attached;
			}
			else if (arg.StartsWith('-'))
			{
				output.WriteLine($"tail: invalid option '{arg}'");
				return;
			}
			else
			{
				target = arg;
			}
		}

		if (target is null)
		{
			output.WriteLine("tail: name a file; the demo shell has no standard input");
			return;
		}

		string path = PathOf(target);
		DemoFileData file;
		try
		{
			file = _tree.Read(path, _account);
		}
		catch (RemoteFileSystemException ex)
		{
			output.WriteLine($"tail: cannot open '{target}' for reading: {ex.Message}");
			return;
		}

		if (DemoText.TryDecode(file.Content, out string? text) && text.Length > 0)
		{
			string[] lines = text.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
			for (int i = Math.Max(0, lines.Length - count); i < lines.Length; i++)
			{
				output.WriteLine(lines[i]);
			}
		}

		if (!follow)
		{
			return;
		}

		// Nothing appends to demo files, so following prints a made-up line every 200 ms until Ctrl+C.
		ulong sequence = DemoRandom.Seed(path);
		while (true)
		{
			await output.FlushAsync(cancellationToken);
			await Task.Delay(FollowInterval, _timeProvider, cancellationToken);
			output.WriteLine(DemoLog.Line(sequence++, Now, _hostName, _login.Name, color: true));
		}
	}

	// Roughly how ls sorts in a UTF-8 locale: case and leading dots do not count.
	private static int CompareNames(string a, string b)
	{
		int result = string.Compare(a.TrimStart('.'), b.TrimStart('.'), StringComparison.OrdinalIgnoreCase);
		return result != 0 ? result : string.CompareOrdinal(a, b);
	}

	private static string HumanSize(long bytes)
	{
		if (bytes < 1024)
		{
			return bytes.ToString(CultureInfo.InvariantCulture);
		}

		double value = bytes;
		int unit = -1;
		do
		{
			value /= 1024;
			unit++;
		}
		while (value >= 1024 && unit < SizeUnits.Length - 1);

		// ls -h rounds up, with one decimal below ten.
		return value < 10
			? (Math.Ceiling(value * 10) / 10).ToString("0.0", CultureInfo.InvariantCulture) + SizeUnits[unit]
			: Math.Ceiling(value).ToString("0", CultureInfo.InvariantCulture) + SizeUnits[unit];
	}

	// GNU ls quotes names a shell would split or expand.
	private static string Quote(string name)
	{
		if (!name.AsSpan().ContainsAny(NeedsQuoting))
		{
			return name;
		}

		return name.Contains('\'', StringComparison.Ordinal)
			? "\"" + name.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
			: "'" + name + "'";
	}

	private static string Colored(RemoteFileEntry entry, string name)
	{
		UnixFileMode mode = entry.Permissions ?? UnixFileMode.None;
		string? color = entry.Kind switch
		{
			RemoteEntryKind.SymbolicLink => entry.LinkTargetKind is null ? BrokenLinkColor : Ansi.BoldCyan,
			RemoteEntryKind.Directory => (mode & (UnixFileMode.StickyBit | UnixFileMode.OtherWrite)) == (UnixFileMode.StickyBit | UnixFileMode.OtherWrite)
				? OpenFolderColor
				: Ansi.BoldBlue,
			_ when (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0 => Ansi.BoldGreen,
			_ when HasExtension(entry.Name, ArchiveExtensions) => Ansi.BoldRed,
			_ when HasExtension(entry.Name, ImageExtensions) => ImageColor,
			_ => null,
		};

		return color is null ? name : color + name + Ansi.Reset;
	}

	private static bool HasExtension(string name, string[] extensions) =>
		Array.Exists(extensions, extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
}
