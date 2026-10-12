using System.Globalization;
using System.Text;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.DevHost.Demo.FileSystem;

namespace Mokaterm.DevHost.Demo.Docker;

/// <summary>
/// The SSH module's file scripts run inside a demo container (<c>docker exec NAME sh -c SCRIPT sh ARG...</c>), answered
/// from the container's tree the way the script itself would print: the output marker, then NUL-terminated records in
/// GNU find's format, a piece of a file, or one of the exit codes 101 to 105. The scripts are told apart by a phrase only
/// each one has, the way the host scripts are. A write arrives through the host's input script, with the staged file read
/// from the session's own tree.
/// </summary>
internal sealed partial class DemoDockerEngine
{
	private const string NoShell =
		"OCI runtime exec failed: exec failed: unable to start container process: exec: \"sh\": executable file not found in $PATH: unknown";

	private static readonly byte[] Marker = [.. "MOKATERM-OUT"u8, 0];

	private readonly Dictionary<string, DemoFileTree?> _containerTrees = new(StringComparer.Ordinal);

	private static bool IsFileScript(string script) => script.Contains("printf 'MOKATERM-OUT", StringComparison.Ordinal);

	private static bool IsInputScript(string script) => script.Contains("exec \"$@\" < \"$f\"", StringComparison.Ordinal);

	/// <summary><c>sh -c INPUT-SCRIPT sh FILE docker exec -i ...</c>: the docker command with the staged file on its stdin.</summary>
	private DemoDockerResult WithInput(string[] arguments)
	{
		if (arguments is not [var staged, "docker", "exec", .. var execution])
		{
			return DemoDockerResult.Fail(2, "sh: 1: the demo only pipes files into docker exec");
		}

		return _readBytes(staged) is { } input
			? Exec(execution, input)
			: DemoDockerResult.Fail(2, $"sh: 1: cannot open {staged}: No such file");
	}

	private DemoDockerResult ContainerScript(DemoDockerContainer container, string script, string[] arguments, byte[]? input)
	{
		if (!_containerTrees.TryGetValue(container.Name, out DemoFileTree? tree))
		{
			tree = DemoContainerFiles.For(container, _time);
			_containerTrees[container.Name] = tree;
		}

		if (tree is null)
		{
			return DemoDockerResult.Fail(127, NoShell);
		}

		string first = arguments.Length > 0 ? arguments[0] : "";
		try
		{
			return script switch
			{
				_ when script.Contains("id -un", StringComparison.Ordinal) => Printed("root\0/root\0"),
				_ when script.Contains("getent passwd", StringComparison.Ordinal) => Printed(DemoContainerFiles.Passwd.ReplaceLineEndings("\n") + "\n\0" + DemoContainerFiles.Group.ReplaceLineEndings("\n") + "\n"),
				_ when script.Contains("-mindepth 1 -maxdepth 1", StringComparison.Ordinal) => List(tree, first, arguments.Length > 1 && arguments[1] == "list"),
				_ when script.Contains("pwd -P", StringComparison.Ordinal) => tree.Stat(first, DemoAccount.Root) is null ? Exit(101) : Printed(tree.Resolve(first, DemoAccount.Root) + "\n"),
				_ when script.Contains("head -c", StringComparison.Ordinal) => ReadRange(tree, first, long.Parse(arguments[1], CultureInfo.InvariantCulture), int.Parse(arguments[2], CultureInfo.InvariantCulture)),
				_ when script.Contains("cat > \"$1\"", StringComparison.Ordinal) => Write(tree, arguments, input ?? []),
				_ when script.Contains("exec mkdir", StringComparison.Ordinal) => tree.Stat(first, DemoAccount.Root) is not null ? Exit(103) : Done(() => tree.CreateDirectory(first, DemoAccount.Root)),
				_ when script.Contains("exec rmdir", StringComparison.Ordinal) => tree.Stat(first, DemoAccount.Root) is null ? Exit(101) : Done(() => tree.Delete(first, arguments[1] == "1", DemoAccount.Root)),
				_ when script.Contains("exec mv -f", StringComparison.Ordinal) => Move(tree, first, arguments[1], arguments[2] == "1"),
				_ when script.Contains("cp -a", StringComparison.Ordinal) => Done(() => tree.Copy(first, arguments[1], arguments[2] == "1", DemoAccount.Root)),
				_ when script.Contains("chmod -R", StringComparison.Ordinal) => ChangeMode(tree, first, arguments[1], arguments[2] == "1"),
				_ when script.Contains("chown -R", StringComparison.Ordinal) => ChangeOwner(tree, first, arguments[1], arguments[2] == "1"),
				_ => DemoDockerResult.Fail(2, "sh: 1: the demo container does not run that script"),
			};
		}
		catch (RemoteFileSystemException ex)
		{
			// Every script prints the marker before it touches a file, so a failure comes after it, as the tool's own line.
			return Exit(1, $"sh: {ex.Path ?? first}: {ex.Message}");
		}
	}

	private static DemoDockerResult List(DemoFileTree tree, string path, bool list)
	{
		if (tree.Stat(path, DemoAccount.Root) is not { } entry)
		{
			return Exit(101);
		}

		List<byte> output = [.. Marker, .. "G\0"u8];
		if (!list)
		{
			Record(output, entry, RemotePath.GetName(path));
			return Bytes(output.ToArray());
		}

		if (!entry.IsDirectoryLike)
		{
			return Exit(102);
		}

		foreach (RemoteFileEntry child in tree.ListDirectory(path, DemoAccount.Root))
		{
			Record(output, child, child.Name);
		}

		return Bytes(output.ToArray());
	}

	/// <summary>The fields GNU find prints for <c>%y %s %T@ %m %u %g %l %Y %f</c>, each NUL-terminated.</summary>
	private static void Record(List<byte> output, RemoteFileEntry entry, string name)
	{
		string type = Letter(entry.Kind);
		string target = entry.Kind == RemoteEntryKind.SymbolicLink ? entry.LinkTargetKind is { } kind ? Letter(kind) : "N" : type;
		string[] fields =
		[
			type,
			entry.Size.ToString(CultureInfo.InvariantCulture),
			(entry.LastModified?.ToUnixTimeSeconds() ?? 0).ToString(CultureInfo.InvariantCulture) + ".0000000000",
			Convert.ToString((int)(entry.Permissions ?? UnixFileMode.None) & 0xFFF, 8),
			entry.Owner ?? "root",
			entry.Group ?? "root",
			entry.LinkTarget ?? "",
			target,
			name,
		];

		foreach (string field in fields)
		{
			output.AddRange(Encoding.UTF8.GetBytes(field));
			output.Add(0);
		}
	}

	private static string Letter(RemoteEntryKind kind) => kind switch
	{
		RemoteEntryKind.File => "f",
		RemoteEntryKind.Directory => "d",
		RemoteEntryKind.SymbolicLink => "l",
		_ => "p",
	};

	/// <summary><c>dd bs=65536 skip=BLOCK | head -c COUNT</c>, from the stored bytes repeated up to the file's size as every demo read does.</summary>
	private static DemoDockerResult ReadRange(DemoFileTree tree, string path, long block, int count)
	{
		DemoFileData data;
		try
		{
			data = tree.Read(path, DemoAccount.Root);
		}
		catch (RemoteFileSystemException ex) when (ex.Kind == RemoteFileErrorKind.NotFound)
		{
			return Exit(101);
		}
		catch (RemoteFileSystemException ex) when (ex.Message == "Is a directory")
		{
			return Exit(102);
		}

		long start = Math.Min(block * 65536, data.Size);
		int length = (int)Math.Min(count, data.Size - start);
		byte[] output = new byte[Marker.Length + length];
		Marker.CopyTo(output, 0);
		using GeneratedStream content = new(data.Size, data.Content) { Position = start };
		content.ReadExactly(output.AsSpan(Marker.Length, length));
		return Bytes(output);
	}

	private static DemoDockerResult Write(DemoFileTree tree, string[] arguments, byte[] input)
	{
		string path = arguments[0];
		if (tree.Stat(path, DemoAccount.Root) is { } existing)
		{
			if (existing.Kind == RemoteEntryKind.Directory)
			{
				return Exit(102);
			}

			if (arguments[1] != "1")
			{
				return Exit(103);
			}
		}

		UnixFileMode? mode = arguments[2].Length > 0 ? (UnixFileMode)Convert.ToInt32(arguments[2], 8) : null;
		DateTimeOffset? stamp = arguments[3].Length > 0
			? DateTimeOffset.ParseExact(arguments[3], "yyyyMMddHHmm.ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
			: null;
		DemoNode file = tree.BeginWrite(path, overwrite: true, mode, DemoAccount.Root);
		tree.Append(file, input);
		tree.CompleteWrite(file, stamp);
		return Bytes(Marker);
	}

	private static DemoDockerResult Move(DemoFileTree tree, string source, string destination, bool overwrite)
	{
		if (tree.Stat(source, DemoAccount.Root) is null)
		{
			return Exit(101);
		}

		if (!overwrite && tree.Stat(destination, DemoAccount.Root) is not null)
		{
			return Exit(103);
		}

		return Done(() => tree.Rename(source, destination, overwrite, DemoAccount.Root));
	}

	private static DemoDockerResult ChangeMode(DemoFileTree tree, string path, string mode, bool recursive)
	{
		if (!mode.All(digit => digit is >= '0' and <= '7'))
		{
			return Exit(1, "chmod: the demo container only takes numeric modes");
		}

		return Done(() => tree.SetPermissions(path, (UnixFileMode)Convert.ToInt32(mode, 8), recursive, DemoAccount.Root));
	}

	private static DemoDockerResult ChangeOwner(DemoFileTree tree, string path, string owners, bool recursive)
	{
		string[] parts = owners.Split(':', 2);
		return Done(() => tree.SetOwner(path, parts[0], parts.Length > 1 ? parts[1] : null, recursive, DemoAccount.Root));
	}

	private static DemoDockerResult Done(Action change)
	{
		change();
		return Bytes(Marker);
	}

	private static DemoDockerResult Printed(string text) => Bytes([.. Marker, .. Encoding.UTF8.GetBytes(text)]);

	private static DemoDockerResult Bytes(byte[] output) => new(0, "", "") { Bytes = output };

	private static DemoDockerResult Exit(int code, string error = "") => new(code, "", error.Length > 0 ? error + "\n" : "") { Bytes = Marker };
}
