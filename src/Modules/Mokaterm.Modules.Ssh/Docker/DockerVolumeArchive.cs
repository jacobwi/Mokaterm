using System.Globalization;
using System.Security.Cryptography;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ssh.Docker;

// Volume backup and restore through a helper container that is created and never started: docker cp reads and writes
// a container's volumes whether or not it runs, and makes and reads the tar itself, so the image needs no tar or
// shell and any image already on the host serves. The archive goes between docker and a file on the docker host
// inside one script, never through the exec channel's captured output, which is capped far below a volume's size.
internal static class DockerVolumeArchive
{
	public const string HelperPrefix = "mokaterm-volume-";

	public const string MountPath = "/mokaterm-volume";

	// What docker cp - extracts: the engine API takes a tar plain or compressed with gzip, bzip2 or xz.
	public static readonly IReadOnlyList<string> RestoreExtensions = [".tar", ".tar.gz", ".tgz", ".tar.bz2", ".tbz2", ".tar.xz", ".txz"];

	public static readonly TimeSpan TimeLimit = TimeSpan.FromHours(6);

	// Arguments: helper, volume, image id, target, partial file, 1 to gzip, seconds. The partial file sits beside the
	// target and becomes it only once docker cp and gzip both succeeded; whatever happens the helper goes.
	internal static readonly string BackupScript = """
		helper=$1 volume=$2 image=$3 out=$4 part=$5 gzip=$6 limit=$7
		copier= ticker= kept=
		finish() {
			[ -z "$ticker" ] || kill "$ticker" 2>/dev/null
			[ -z "$copier" ] || kill "$copier" 2>/dev/null
			docker rm --force --volumes "$helper" >/dev/null 2>&1
			rm -f -- "$part.rc"
			[ -n "$kept" ] || rm -f -- "$part"
		}
		late() {
			echo "It ran past the $((limit / 60)) minutes it may take, so it was stopped." >&2
			exit 124
		}
		trap "" PIPE
		trap finish EXIT
		trap "exit 143" HUP INT TERM
		trap late USR1
		if [ -e "$out" ]; then
			echo "$out exists already, and a backup never replaces a file." >&2
			exit 3
		fi
		umask 077
		docker create --name "$helper" --label com.mokaterm.helper=volume-backup --network none --mount "type=volume,source=$volume,target=/mokaterm-volume,readonly,volume-nocopy" "$image" /mokaterm-never-runs >/dev/null || exit $?
		echo "Copying $volume out of $helper, a container that is never started."
		if [ "$gzip" = 1 ]; then
			{ docker cp "$helper:/mokaterm-volume/." -; echo "$?" > "$part.rc"; } | gzip -c > "$part" &
		else
			docker cp "$helper:/mokaterm-volume/." - > "$part" &
		fi
		copier=$!
		(
			waited=0
			while [ "$waited" -lt "$limit" ]; do
				sleep 10 </dev/null >/dev/null 2>&1
				waited=$((waited + 10))
				size=$(du -h -- "$part" 2>/dev/null | cut -f1)
				[ -z "$size" ] || echo "Written so far: $size"
			done
			kill -USR1 $$ 2>/dev/null
		) &
		ticker=$!
		wait "$copier"
		status=$?
		copier=
		if [ "$gzip" = 1 ] && [ "$status" -eq 0 ]; then
			status=$(cat -- "$part.rc" 2>/dev/null)
			status=${status:-1}
		fi
		[ "$status" -eq 0 ] || exit "$status"
		if [ -n "${SUDO_UID:-}" ]; then
			chown -- "$SUDO_UID:${SUDO_GID:-$SUDO_UID}" "$part"
		fi
		if [ -e "$out" ]; then
			echo "Something else wrote $out while the backup ran, so the backup was not put there." >&2
			exit 3
		fi
		mv -- "$part" "$out" || exit $?
		kept=1
		echo "Wrote $out, $(du -h -- "$out" | cut -f1)."
		""".ReplaceLineEndings("\n");

	// Arguments: helper, volume, image id, archive, 1 to create the volume first, seconds. A volume this script created
	// is removed again when the restore fails, so trying again with the same name works.
	internal static readonly string RestoreScript = """
		helper=$1 volume=$2 image=$3 archive=$4 create=$5 limit=$6
		copier= ticker= made=
		finish() {
			[ -z "$ticker" ] || kill "$ticker" 2>/dev/null
			[ -z "$copier" ] || kill "$copier" 2>/dev/null
			docker rm --force --volumes "$helper" >/dev/null 2>&1
			[ -z "$made" ] || docker volume rm "$volume" >/dev/null 2>&1
		}
		late() {
			echo "It ran past the $((limit / 60)) minutes it may take, so it was stopped." >&2
			exit 124
		}
		trap "" PIPE
		trap finish EXIT
		trap "exit 143" HUP INT TERM
		trap late USR1
		if [ ! -f "$archive" ] || [ ! -r "$archive" ]; then
			echo "$archive is not a file this account can read." >&2
			exit 2
		fi
		if [ "$create" = 1 ]; then
			if docker volume inspect "$volume" >/dev/null 2>&1; then
				echo "A volume named $volume exists already." >&2
				exit 3
			fi
			docker volume create "$volume" >/dev/null || exit $?
			made=1
		fi
		docker create --name "$helper" --label com.mokaterm.helper=volume-restore --network none --mount "type=volume,source=$volume,target=/mokaterm-volume,volume-nocopy" "$image" /mokaterm-never-runs >/dev/null || exit $?
		size=$(stat -c %s -- "$archive" 2>/dev/null || wc -c < "$archive")
		echo "Unpacking $archive into $volume through $helper, a container that is never started."
		docker cp --archive - "$helper:/mokaterm-volume" < "$archive" &
		copier=$!
		(
			waited=0
			while [ "$waited" -lt "$limit" ]; do
				sleep 10 </dev/null >/dev/null 2>&1
				waited=$((waited + 10))
				position=$(sed -n "s/^pos:[[:space:]]*//p" "/proc/$copier/fdinfo/0" 2>/dev/null)
				if [ -n "$position" ] && [ "${size:-0}" -gt 0 ] 2>/dev/null; then
					echo "Read $((position * 100 / size))% of the archive"
				fi
			done
			kill -USR1 $$ 2>/dev/null
		) &
		ticker=$!
		wait "$copier"
		status=$?
		copier=
		[ "$status" -eq 0 ] || exit "$status"
		made=
		echo "Restored $archive into $volume."
		""".ReplaceLineEndings("\n");

	public static string NewHelperName() => HelperPrefix + RandomNumberGenerator.GetHexString(12, lowercase: true);

	public static string PartialFile(string target)
	{
		ArgumentNullException.ThrowIfNull(target);
		return RemotePath.Combine(RemotePath.GetParent(target), ".mokaterm-" + RandomNumberGenerator.GetHexString(16, lowercase: true) + ".part");
	}

	// Any image serves, so the smallest one on the host does; an image without a tag is taken only when nothing else is.
	public static DockerImage? PickHelperImage(IReadOnlyList<DockerImage> images)
	{
		ArgumentNullException.ThrowIfNull(images);
		return images
			.Where(image => DockerNames.IsId(BareId(image.Id)))
			.OrderBy(image => image.IsDangling)
			.ThenBy(image => image.SizeBytes ?? long.MaxValue)
			.ThenBy(image => image.Reference, StringComparer.Ordinal)
			.FirstOrDefault();
	}

	public static string DefaultFileName(string volume, DateTimeOffset now, bool gzip) => string.Create(
		CultureInfo.InvariantCulture,
		$"{volume}-{now.ToLocalTime():yyyyMMdd-HHmmss}{(gzip ? ".tar.gz" : ".tar")}");

	public static bool IsGzipName(string name) =>
		name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);

	public static bool IsRestorable(string name) =>
		RestoreExtensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

	public static string? BackupNameProblem(string? name)
	{
		string text = name?.Trim() ?? "";
		if (text.Length == 0)
		{
			return "Name the file, such as data.tar.gz.";
		}

		if (text is "." or ".." || !RemotePath.IsValidName(text) || text.Any(char.IsControl) || text.Length > 255)
		{
			return "A file name has no '/' or control characters, and at most 255 characters.";
		}

		return text.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) || IsGzipName(text)
			? null
			: "End the name in .tar, or .tar.gz to compress it.";
	}

	public static DockerCall Backup(string volume, string imageId, string target, string helper, string partialFile, bool gzip) =>
		Script(BackupScript, helper, [Existing(volume), Id(imageId), Path(target, "backup file"), Path(partialFile, "partial file"), gzip ? "1" : "0"]);

	public static DockerCall Restore(string volume, bool createVolume, string imageId, string archive, string helper) =>
		Script(RestoreScript, helper, [createVolume ? DockerCommands.Checked(volume, DockerNames.NewNameProblem(volume, "volume")) : Existing(volume), Id(imageId), Path(archive, "archive"), createVolume ? "1" : "0"]);

	// The helper by name after a run however it ended, for when the script's own trap never got to run.
	public static DockerCall RemoveHelper(string helper) =>
		new DockerCall { Program = DockerProgram.Docker, Arguments = ["rm", "--force", "--volumes", Helper(helper)] };

	private static DockerCall Script(string script, string helper, IReadOnlyList<string> rest)
	{
		string seconds = ((long)TimeLimit.TotalSeconds).ToString(CultureInfo.InvariantCulture);
		string name = Helper(helper);

		// This side waits a minute longer than the script's own limit, so the script's stop and its message come first.
		return DockerCall.Script(script, [name, .. rest, seconds]) with
		{
			Timeout = TimeLimit + TimeSpan.FromMinutes(1),
			RunsLong = true,
			OutputLimitBytes = DockerCall.LiveOutputBytes,
			Touches = new DockerChangeScope { Containers = new HashSet<string>([name], StringComparer.Ordinal) },
		};
	}

	private static string Helper(string helper) =>
		helper.StartsWith(HelperPrefix, StringComparison.Ordinal) && DockerNames.NewNameProblem(helper, "container") is null
			? helper
			: throw new ArgumentException("That is not a helper container's name.", nameof(helper));

	// The volume goes inside --mount's comma separated list; the existing name rule already keeps commas and '=' out.
	private static string Existing(string volume) => DockerCommands.Checked(volume, DockerNames.ExistingNameProblem(volume, "volume"));

	private static string Id(string imageId)
	{
		string bare = BareId(imageId);
		return DockerNames.IsId(bare) ? bare : throw new ArgumentException("That is not an image id.", nameof(imageId));
	}

	private static string BareId(string id) => id.StartsWith("sha256:", StringComparison.Ordinal) ? id[7..] : id;

	private static string Path(string path, string what) => DockerCommands.Checked(path, DockerNames.PathProblem(path, what));
}
