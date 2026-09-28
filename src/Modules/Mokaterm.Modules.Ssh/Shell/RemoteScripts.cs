namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>
/// POSIX sh scripts run on the server through <c>sh -c SCRIPT sh ARG...</c>. Paths only ever arrive as positional
/// arguments. Every script starts by printing <see cref="OutputMarker"/> so its output can be told apart from noise that
/// login scripts print, and exits with the codes below for conditions callers map to <c>RemoteFileErrorKind</c> values.
/// </summary>
internal static class RemoteScripts
{
	/// <summary>Printed and NUL-terminated on stdout before anything else a script writes.</summary>
	public const string OutputMarker = "MOKATERM-OUT";

	/// <summary>Printed on its own stderr line when a script starts under sudo, which proves sudo let it run.</summary>
	public const string ElevatedMarker = "MOKATERM-ELEVATED";

	public const int ExitNotFound = 101;

	/// <summary>A directory was found where a file was expected, or the reverse.</summary>
	public const int ExitWrongKind = 102;

	public const int ExitAlreadyExists = 103;

	public const int ExitDirectoryNotEmpty = 104;

	private const string ElevatedPreambleText = """
		printf '%s\n' MOKATERM-ELEVATED >&2

		""";

	// C locale for stable messages and parsing, then the output marker.
	private const string Preamble = """
		LC_ALL=C
		export LC_ALL
		printf 'MOKATERM-OUT\0'

		""";

	private const string ListBody = """
		p=$1
		if [ ! -e "$p" ] && [ ! -L "$p" ]; then exit 101; fi
		if [ "$2" = list ] && [ ! -d "$p" ]; then exit 102; fi
		if find / -maxdepth 0 -printf '' >/dev/null 2>&1; then
			printf 'G\0'
			if [ "$2" = list ]; then
				exec find -H "$p" -mindepth 1 -maxdepth 1 -printf '%y\0%s\0%T@\0%m\0%u\0%g\0%l\0%Y\0%f\0'
			fi
			exec find "$p" -maxdepth 0 -printf '%y\0%s\0%T@\0%m\0%u\0%g\0%l\0%Y\0%f\0'
		fi
		printf 'P\0'
		entry() {
			info=$(stat -c '%f %s %Y %u %g %U %G' -- "$1") || return 0
			target=
			kind=
			if [ -L "$1" ]; then
				target=$(readlink -- "$1")
				if [ -d "$1" ]; then kind=d; elif [ -e "$1" ]; then kind=f; else kind=N; fi
			fi
			printf '%s\0%s\0%s\0%s\0' "$info" "$target" "$kind" "${1##*/}"
		}
		if [ "$2" = list ]; then
			for f in "$p"/* "$p"/.[!.]* "$p"/..?*; do
				if [ -e "$f" ] || [ -L "$f" ]; then entry "$f"; fi
			done
		else
			entry "$p"
		fi
		""";

	private const string ResolveBody = """
		if [ ! -e "$1" ]; then exit 101; fi
		if [ -d "$1" ]; then
			cd -P -- "$1" && pwd -P
		else
			readlink -f -- "$1"
		fi
		""";

	private const string LinkTargetsBody = """
		cd -- "$1" || exit 101
		shift
		for f in "$@"; do
			target=$(readlink -- "$f" 2>/dev/null)
			if [ -d "$f" ]; then kind=d; elif [ -e "$f" ]; then kind=f; else kind=N; fi
			printf '%s\0%s\0%s\0' "$f" "$kind" "$target"
		done
		""";

	private const string AccountsBody = """
		getent passwd 2>/dev/null || cat /etc/passwd 2>/dev/null
		printf '\0'
		getent group 2>/dev/null || cat /etc/group 2>/dev/null
		""";

	private const string CreateDirectoryBody = """
		if [ -e "$1" ] || [ -L "$1" ]; then exit 103; fi
		exec mkdir -- "$1"
		""";

	private const string DeleteBody = """
		if [ ! -e "$1" ] && [ ! -L "$1" ]; then exit 101; fi
		if [ -d "$1" ] && [ ! -L "$1" ]; then
			if [ "$2" = 1 ]; then exec rm -rf -- "$1"; fi
			exec rmdir -- "$1"
		fi
		exec rm -f -- "$1"
		""";

	private const string RenameBody = """
		if [ ! -e "$1" ] && [ ! -L "$1" ]; then exit 101; fi
		if [ -e "$2" ] || [ -L "$2" ]; then
			if [ "$3" != 1 ]; then exit 103; fi
			if [ -L "$2" ]; then
				rm -f -- "$2" || exit 1
			elif [ -d "$2" ]; then
				rmdir -- "$2" 2>/dev/null || exit 104
			fi
		fi
		exec mv -f -- "$1" "$2"
		""";

	// find without -L never follows links, and "! -type l" keeps chmod from changing what a link inside points at.
	private const string ChangeModeBody = """
		if [ ! -e "$1" ] && [ ! -L "$1" ]; then exit 101; fi
		case $4 in
		d)
			if [ "$3" = 1 ]; then exec find "$1" -type d -exec chmod -- "$2" {} +; fi
			if [ -d "$1" ] && [ ! -L "$1" ]; then exec chmod -- "$2" "$1"; fi
			;;
		f)
			if [ "$3" = 1 ]; then exec find "$1" ! -type d ! -type l -exec chmod -- "$2" {} +; fi
			if [ ! -d "$1" ] && [ ! -L "$1" ]; then exec chmod -- "$2" "$1"; fi
			;;
		*)
			if [ "$3" = 1 ]; then exec chmod -R -- "$2" "$1"; fi
			exec chmod -- "$2" "$1"
			;;
		esac
		""";

	private const string ChangeOwnerBody = """
		if [ ! -e "$1" ] && [ ! -L "$1" ]; then exit 101; fi
		if [ "$3" = 1 ]; then exec chown -R -- "$2" "$1"; fi
		exec chown -- "$2" "$1"
		""";

	private const string ChangeGroupBody = """
		if [ ! -e "$1" ] && [ ! -L "$1" ]; then exit 101; fi
		if [ "$3" = 1 ]; then exec chgrp -R -- "$2" "$1"; fi
		exec chgrp -- "$2" "$1"
		""";

	private const string ReadBody = """
		if [ ! -e "$1" ]; then exit 101; fi
		if [ -d "$1" ]; then exit 102; fi
		exec cat -- "$1"
		""";

	private const string PlaceUploadBody = """
		staging=$1
		target=$2
		trap 'rm -f -- "$staging"' EXIT
		if [ -d "$target" ] && [ ! -L "$target" ]; then exit 102; fi
		if [ -e "$target" ] || [ -L "$target" ]; then
			if [ "$3" != 1 ]; then exit 103; fi
			cat -- "$staging" > "$target" || exit 1
			if [ -n "$4" ]; then chmod -- "$4" "$target" || exit 1; fi
		else
			mv -f -- "$staging" "$target" || exit 1
			chown -- 0:0 "$target" || exit 1
			chmod -- "${4:-644}" "$target" || exit 1
			if command -v restorecon >/dev/null 2>&1; then restorecon -- "$target" 2>/dev/null; fi
		fi
		if [ -n "$5" ]; then TZ=UTC0 touch -t "$5" -- "$target" || exit 1; fi
		""";

	/// <summary>Written first under sudo, ahead of the script itself.</summary>
	public static readonly string ElevatedPreamble = Normalize(ElevatedPreambleText);

	/// <summary>
	/// <c>$1</c> path, <c>$2</c> <c>list</c> or <c>stat</c>. Prints a format tag, then NUL-terminated records: with GNU find
	/// (<c>G</c>) type, size, mtime, octal mode, user, group, link target, target type, name; otherwise (<c>P</c>, BusyBox)
	/// <c>stat -c '%f %s %Y %u %g %U %G'</c>, link target, target type (<c>d</c>, <c>f</c>, <c>N</c>), name.
	/// </summary>
	public static readonly string List = Normalize(Preamble + ListBody);

	/// <summary><c>$1</c> path. Prints the physical absolute path, resolving links.</summary>
	public static readonly string Resolve = Normalize(Preamble + ResolveBody);

	/// <summary><c>$1</c> directory, then link names inside it. Prints name, target type (<c>d</c>, <c>f</c>, <c>N</c>) and literal target per link.</summary>
	public static readonly string LinkTargets = Normalize(Preamble + LinkTargetsBody);

	/// <summary>Prints the passwd database, a NUL, then the group database.</summary>
	public static readonly string Accounts = Normalize(Preamble + AccountsBody);

	/// <summary><c>$1</c> path.</summary>
	public static readonly string CreateDirectory = Normalize(Preamble + CreateDirectoryBody);

	/// <summary><c>$1</c> path, <c>$2</c> <c>1</c> to delete directories recursively. Links are removed, never followed.</summary>
	public static readonly string Delete = Normalize(Preamble + DeleteBody);

	/// <summary><c>$1</c> source, <c>$2</c> destination, <c>$3</c> <c>1</c> to replace an existing destination.</summary>
	public static readonly string Rename = Normalize(Preamble + RenameBody);

	/// <summary>
	/// <c>$1</c> path, <c>$2</c> chmod mode (octal or symbolic), <c>$3</c> <c>1</c> for recursive, <c>$4</c> <c>d</c> to
	/// change only folders, <c>f</c> only what is neither a folder nor a link, empty for everything.
	/// </summary>
	public static readonly string ChangeMode = Normalize(Preamble + ChangeModeBody);

	/// <summary><c>$1</c> path, <c>$2</c> <c>owner[:group]</c>, <c>$3</c> <c>1</c> for recursive.</summary>
	public static readonly string ChangeOwner = Normalize(Preamble + ChangeOwnerBody);

	/// <summary><c>$1</c> path, <c>$2</c> group, <c>$3</c> <c>1</c> for recursive. Owners stay as they are.</summary>
	public static readonly string ChangeGroup = Normalize(Preamble + ChangeGroupBody);

	/// <summary><c>$1</c> path. Streams the file after the output marker.</summary>
	public static readonly string Read = Normalize(Preamble + ReadBody);

	/// <summary>
	/// <c>$1</c> staging file, <c>$2</c> destination, <c>$3</c> <c>1</c> to replace, <c>$4</c> octal mode or empty,
	/// <c>$5</c> <c>touch -t</c> UTC stamp or empty. An existing destination is rewritten in place so it keeps its owner and
	/// mode; a new one is moved in and handed to root. The staging file is removed on every exit.
	/// </summary>
	public static readonly string PlaceUpload = Normalize(Preamble + PlaceUploadBody);

	// Raw strings take the line endings of this source file, and a carriage return breaks sh.
	private static string Normalize(string script) => script.ReplaceLineEndings("\n");
}
