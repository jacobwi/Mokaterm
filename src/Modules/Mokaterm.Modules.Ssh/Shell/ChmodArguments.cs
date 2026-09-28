using System.Globalization;
using System.Text;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>Writes a <see cref="PermissionChange"/> as the arguments <see cref="RemoteScripts.ChangeMode"/> takes.</summary>
internal static class ChmodArguments
{
	/// <summary>
	/// The mode for chmod: five octal digits for a whole mode, otherwise symbolic clauses such as <c>g+w,o-rwx</c> that
	/// leave the other bits alone.
	/// </summary>
	/// <returns>Null when the change sets and clears nothing.</returns>
	public static string? Mode(PermissionChange change)
	{
		ArgumentNullException.ThrowIfNull(change);
		UnixFileMode mask = change.Mask & PermissionChange.AllBits;
		if (mask == UnixFileMode.None)
		{
			return null;
		}

		UnixFileMode mode = change.Mode & mask;
		if (!change.NeedsCurrentMode)
		{
			// GNU chmod keeps a directory's setuid and setgid bits for a numeric mode of fewer than five digits.
			return Convert.ToString((int)mode, 8).PadLeft(5, '0');
		}

		List<string> additions = [];
		List<string> removals = [];
		AddClass('u', UnixFileMode.UserRead, UnixFileMode.UserWrite, UnixFileMode.UserExecute);
		AddClass('g', UnixFileMode.GroupRead, UnixFileMode.GroupWrite, UnixFileMode.GroupExecute);
		AddClass('o', UnixFileMode.OtherRead, UnixFileMode.OtherWrite, UnixFileMode.OtherExecute);

		// "a" for the sticky bit: BusyBox does not count it as one of the "o" bits.
		AddSpecial('u', UnixFileMode.SetUser, 's');
		AddSpecial('g', UnixFileMode.SetGroup, 's');
		AddSpecial('a', UnixFileMode.StickyBit, 't');

		// Additions first: chmod decides X from the mode the earlier clauses left, which a removal may already have changed.
		return string.Join(',', additions.Concat(removals));

		void AddClass(char who, UnixFileMode read, UnixFileMode write, UnixFileMode execute)
		{
			StringBuilder set = new();
			StringBuilder clear = new();
			Sort(read, 'r', set, clear);
			Sort(write, 'w', set, clear);
			Sort(execute, change.ConditionalExecute ? 'X' : 'x', set, clear, clearLetter: 'x');
			Add(who, set, clear);
		}

		void AddSpecial(char who, UnixFileMode bit, char letter)
		{
			StringBuilder set = new();
			StringBuilder clear = new();
			Sort(bit, letter, set, clear);
			Add(who, set, clear);
		}

		void Sort(UnixFileMode bit, char letter, StringBuilder set, StringBuilder clear, char? clearLetter = null)
		{
			if ((mask & bit) == UnixFileMode.None)
			{
				return;
			}

			if ((mode & bit) != UnixFileMode.None)
			{
				set.Append(letter);
			}
			else
			{
				clear.Append(clearLetter ?? letter);
			}
		}

		void Add(char who, StringBuilder set, StringBuilder clear)
		{
			if (set.Length > 0)
			{
				additions.Add(string.Create(CultureInfo.InvariantCulture, $"{who}+{set}"));
			}

			if (clear.Length > 0)
			{
				removals.Add(string.Create(CultureInfo.InvariantCulture, $"{who}-{clear}"));
			}
		}
	}

	/// <summary><c>d</c> for folders only, <c>f</c> for files only, empty for everything.</summary>
	public static string Targets(PermissionTargets targets) => targets switch
	{
		PermissionTargets.Folders => "d",
		PermissionTargets.Files => "f",
		_ => "",
	};
}
