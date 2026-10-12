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

	/// <summary>
	/// The server has no tool the script needs, such as anything that computes SHA-256. A script that can say which tool
	/// prints that on its own stderr line first.
	/// </summary>
	public const int ExitUnsupported = 105;

	/// <summary>The block <see cref="ReadRange"/> counts its offset in, which is also what its <c>dd</c> reads at a time.</summary>
	public const int ReadBlockBytes = 64 * 1024;

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

	// tail counts from 1, so the byte at offset N is +N+1, which the caller passes.
	private const string ReadFromBody = """
		if [ ! -e "$1" ]; then exit 101; fi
		if [ -d "$1" ]; then exit 102; fi
		exec tail -c "+$2" -- "$1"
		""";

	// GNU and BusyBox have sha256sum, macOS shasum, the BSDs sha256; openssl is the last resort.
	private const string ChecksumBody = """
		if [ ! -e "$1" ]; then exit 101; fi
		if [ -d "$1" ]; then exit 102; fi
		if command -v sha256sum >/dev/null 2>&1; then exec sha256sum -- "$1"; fi
		if command -v shasum >/dev/null 2>&1; then exec shasum -a 256 -- "$1"; fi
		if command -v sha256 >/dev/null 2>&1; then exec sha256 -q -- "$1"; fi
		if command -v openssl >/dev/null 2>&1; then exec openssl dgst -sha256 -r "$1"; fi
		exit 105
		""";

	// A folder copied over a folder merges into it ("$1/." copies the contents), the way a file manager's overwrite does;
	// cp given an existing folder as its target would put the copy inside it instead. A link in the way is removed first,
	// or cp would write through it to whatever it points at.
	private const string CopyBody = """
		if [ ! -e "$1" ] && [ ! -L "$1" ]; then exit 101; fi
		if [ -e "$2" ] || [ -L "$2" ]; then
			if [ "$3" != 1 ]; then exit 103; fi
			if [ -d "$1" ] && [ ! -L "$1" ]; then
				if [ ! -d "$2" ] || [ -L "$2" ]; then
					echo 'A folder cannot replace a file.' >&2
					exit 102
				fi
				exec cp -a -- "$1/." "$2"
			fi
			if [ -d "$2" ] && [ ! -L "$2" ]; then
				echo 'A file cannot replace a folder.' >&2
				exit 102
			fi
			if [ -L "$2" ]; then rm -f -- "$2" || exit 1; fi
		fi
		exec cp -a -- "$1" "$2"
		""";

	// The archive is created empty with noclobber first, so a file that appears meanwhile is never written over, and
	// what tar leaves after a failure is only ever this script's own file. Entries arrive as ./name, which no tar reads
	// as an option.
	private const string CompressBody = """
		cd -- "$1" || exit 101
		archive=$2
		shift 2
		if [ -e "$archive" ] || [ -L "$archive" ]; then exit 103; fi
		if ! command -v tar >/dev/null 2>&1; then
			printf '%s\n' 'The server has no tar command, which making an archive needs.' >&2
			exit 105
		fi
		set -C
		if ! : > "$archive"; then
			if [ -e "$archive" ] || [ -L "$archive" ]; then exit 103; fi
			exit 1
		fi
		set +C
		tar -czf "$archive" "$@"
		status=$?
		if [ "$status" -ne 0 ]; then rm -f -- "$archive"; fi
		exit "$status"
		""";

	// The folder is new and the script's own, so a failed unpack removes it rather than leaving half an archive. -o keeps
	// the files owned by whoever unpacks them, which matters as root: GNU tar would otherwise hand them to the owners the
	// archive names. unzip -n never asks about a name it already wrote, because nothing could answer on stdin, and its
	// exit status 1 only means warnings.
	private const string ExtractBody = """
		archive=$1
		target=$2
		if [ ! -e "$archive" ]; then exit 101; fi
		if [ -d "$archive" ]; then exit 102; fi
		if [ -e "$target" ] || [ -L "$target" ]; then exit 103; fi
		need() {
			for tool in "$@"; do
				if command -v "$tool" >/dev/null 2>&1; then return 0; fi
			done
			printf 'The server has no %s command, which this archive needs.\n' "$1" >&2
			exit 105
		}
		case $3 in
			zip) need unzip ;;
			gz) need tar; need gzip gunzip ;;
			bz2) need tar; need bzip2 bunzip2 ;;
			xz) need tar; need xz unxz ;;
			*) need tar ;;
		esac
		mkdir -- "$target" || exit 1
		case $3 in
			zip) unzip -q -n "$archive" -d "$target" ;;
			gz) tar -x -z -o -f "$archive" -C "$target" ;;
			bz2) tar -x -j -o -f "$archive" -C "$target" ;;
			xz) tar -x -J -o -f "$archive" -C "$target" ;;
			*) tar -x -o -f "$archive" -C "$target" ;;
		esac
		status=$?
		if [ "$3" = zip ] && [ "$status" -eq 1 ]; then status=0; fi
		if [ "$status" -ne 0 ]; then rm -rf -- "$target"; fi
		exit "$status"
		""";

	// id -un fails for a uid the passwd file does not name, which a container run with --user often has.
	private const string IdentityBody = """
		u=$(id -un 2>/dev/null) || u=$(whoami 2>/dev/null) || u=$(id -u 2>/dev/null) || u=
		printf '%s\0%s\0' "$u" "$HOME"
		""";

	// dd skips by seeking on both GNU and BusyBox, and head -c keeps reading until it has its count, which dd's count
	// would not do on a file that answers each read short (/proc). The pipeline exits with head's status whatever dd
	// ran into, so a file the account cannot read is refused before either starts.
	private const string ReadRangeBody = """
		if [ ! -e "$1" ]; then exit 101; fi
		if [ -d "$1" ]; then exit 102; fi
		if [ ! -r "$1" ]; then printf '%s: Permission denied\n' "$1" >&2; exit 1; fi
		if command -v dd >/dev/null 2>&1; then
			dd if="$1" bs=65536 skip="$2" 2>/dev/null | head -c "$3"
		else
			tail -c "+$(($2 * 65536 + 1))" -- "$1" | head -c "$3"
		fi
		""";

	// Written in place, as a replace as root is: the file keeps its inode, owner and mode, which a single file mounted
	// into a container needs, since a rename over a mount point fails. noclobber keeps a file that appeared after the check.
	private const string WriteInputBody = """
		if [ -d "$1" ] && [ ! -L "$1" ]; then exit 102; fi
		if [ -e "$1" ] || [ -L "$1" ]; then
			if [ "$2" != 1 ]; then exit 103; fi
		else
			set -C
		fi
		cat > "$1" || exit 1
		if [ -n "$3" ]; then chmod -- "$3" "$1" || exit 1; fi
		if [ -n "$4" ]; then TZ=UTC0 touch -t "$4" -- "$1" 2>/dev/null; fi
		exit 0
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

	/// <summary><c>$1</c> path, <c>$2</c> the offset to start at plus one. Streams the rest of the file after the output marker.</summary>
	public static readonly string ReadFrom = Normalize(Preamble + ReadFromBody);

	/// <summary>
	/// <c>$1</c> path. Prints the file's SHA-256 as hex at the start of the output, followed by whatever the tool adds (a
	/// file name, or nothing). Exits with <see cref="ExitUnsupported"/> when the server has no tool for it.
	/// </summary>
	public static readonly string Checksum = Normalize(Preamble + ChecksumBody);

	/// <summary>
	/// <c>$1</c> source, <c>$2</c> destination, <c>$3</c> <c>1</c> to replace. Runs <c>cp -a</c>: folders with everything
	/// inside, modes, times and links kept. A folder replacing a folder merges into it.
	/// </summary>
	public static readonly string Copy = Normalize(Preamble + CopyBody);

	/// <summary>
	/// <c>$1</c> folder, <c>$2</c> the new archive's path, then the entries of the folder as <c>./name</c>. Writes a
	/// gzip-compressed tar and never replaces anything.
	/// </summary>
	public static readonly string Compress = Normalize(Preamble + CompressBody);

	/// <summary>
	/// <c>$1</c> archive, <c>$2</c> the new folder to unpack into, <c>$3</c> <c>tar</c>, <c>gz</c>, <c>bz2</c>, <c>xz</c> or
	/// <c>zip</c>. Exits with <see cref="ExitUnsupported"/> and the missing tool's name on stderr when the server lacks it.
	/// </summary>
	public static readonly string Extract = Normalize(Preamble + ExtractBody);

	/// <summary>
	/// <c>$1</c> staging file, <c>$2</c> destination, <c>$3</c> <c>1</c> to replace, <c>$4</c> octal mode or empty,
	/// <c>$5</c> <c>touch -t</c> UTC stamp or empty. An existing destination is rewritten in place so it keeps its owner and
	/// mode; a new one is moved in and handed to root. The staging file is removed on every exit.
	/// </summary>
	public static readonly string PlaceUpload = Normalize(Preamble + PlaceUploadBody);

	/// <summary>
	/// No arguments. Prints the account the script runs as, by name where it has one and by number otherwise, then
	/// <c>$HOME</c>, each NUL-terminated.
	/// </summary>
	public static readonly string Identity = Normalize(Preamble + IdentityBody);

	/// <summary>
	/// <c>$1</c> path, <c>$2</c> where to start in blocks of <see cref="ReadBlockBytes"/>, <c>$3</c> how many bytes. Prints
	/// that part of the file after the output marker; fewer bytes than asked for means the file ends there.
	/// </summary>
	public static readonly string ReadRange = Normalize(Preamble + ReadRangeBody);

	/// <summary>
	/// <c>$1</c> path, <c>$2</c> <c>1</c> to replace, <c>$3</c> octal mode or empty, <c>$4</c> <c>touch -t</c> UTC stamp or
	/// empty. Writes stdin to the file in place, so an existing one keeps its owner and mode; the time is set where
	/// <c>touch</c> can.
	/// </summary>
	public static readonly string WriteInput = Normalize(Preamble + WriteInputBody);

	/// <summary>
	/// Whether <paramref name="script"/> reads or writes file contents, which can take minutes on a big file or tree, so
	/// its channel counts as long in <see cref="ExecChannelBudget"/>. A recursive delete or mode change walks a tree but
	/// touches no contents, and counts as short.
	/// </summary>
	public static bool RunsLong(string script) =>
		script == Copy
		|| script == Compress
		|| script == Extract
		|| script == Checksum
		|| script == PlaceUpload
		|| script == Read
		|| script == ReadFrom;

	// Raw strings take the line endings of this source file, and a carriage return breaks sh.
	private static string Normalize(string script) => script.ReplaceLineEndings("\n");
}
