using System.Globalization;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// Small scripts about the docker host's own files, run through the same shell as the docker commands (so through sudo
/// when they are). Paths only ever arrive as positional arguments; the scripts' text is fixed.
/// </summary>
internal static class DockerHostScripts
{
	/// <summary>The names compose looks for in a folder, in the order it prefers them.</summary>
	public static readonly IReadOnlyList<string> ComposeFileNames = ["compose.yaml", "compose.yml", "docker-compose.yaml", "docker-compose.yml"];

	/// <summary>Where compose projects usually live, searched after the login's own home.</summary>
	public static readonly IReadOnlyList<string> SearchRoots = ["/opt", "/srv", "/var/www", "/docker", "/data"];

	/// <summary>How many folders deep a search goes under each root.</summary>
	public const int SearchDepth = 4;

	/// <summary>The most a search may take per root before <c>timeout</c> (where the host has it) stops it.</summary>
	public const int SearchSecondsPerRoot = 20;

	/// <summary>The most files a search reports.</summary>
	public const int MaxFound = 500;

	/// <summary>One character per path: <c>1</c> for a regular file that exists, <c>0</c> for anything else.</summary>
	private const string FileStatesScript = "for f do if [ -f \"$f\" ]; then printf 1; else printf 0; fi; done";

	/// <summary>
	/// The login's home and the roots given, each searched to a depth, skipping folders that are never compose
	/// projects and are often huge. <c>timeout</c> stops a slow root where the host has it; the call's own time limit
	/// stops the rest. Line endings are made LF here, since this file is CRLF and sh reads a CR as part of a word.
	/// </summary>
	private static readonly string FindScript = FindScriptText.ReplaceLineEndings("\n");

	private const string FindScriptText = """
		depth=$1
		limit=$2
		shift 2
		if command -v timeout >/dev/null 2>&1; then t="timeout $limit"; else t=""; fi
		for root in "$HOME" "$@"; do
			[ -d "$root" ] || continue
			$t find "$root" -maxdepth "$depth" '(' -name node_modules -o -name .git -o -name proc -o -name sys -o -name overlay2 ')' -prune -o -type f '(' -name compose.yaml -o -name compose.yml -o -name docker-compose.yaml -o -name docker-compose.yml ')' -print 2>/dev/null
		done
		exit 0
		""";

	/// <summary>Asks which of <paramref name="paths"/> exist as regular files, in one command.</summary>
	public static DockerCall FileStates(IReadOnlyList<string> paths)
	{
		ArgumentNullException.ThrowIfNull(paths);
		foreach (string path in paths)
		{
			_ = DockerCommands.Checked(path, Mokaterm.Abstractions.Containers.DockerNames.PathProblem(path, "file"));
		}

		return DockerCall.Script(FileStatesScript, paths) with { OutputLimitBytes = Math.Max(paths.Count, 1) + 1024 };
	}

	/// <summary>Reads what <see cref="FileStates"/> printed: one answer per path, in order; a missing answer is "missing".</summary>
	public static IReadOnlyList<bool> ParseFileStates(string output, int count)
	{
		ArgumentNullException.ThrowIfNull(output);
		string answers = output.Trim();
		return [.. Enumerable.Range(0, count).Select(index => index < answers.Length && answers[index] == '1')];
	}

	/// <summary>Searches the usual roots for compose files.</summary>
	public static DockerCall FindComposeFiles(IReadOnlyList<string>? roots = null, int depth = SearchDepth)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(depth, 1);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(depth, 8);
		IReadOnlyList<string> searched = roots ?? SearchRoots;
		foreach (string root in searched)
		{
			_ = DockerCommands.Checked(root, Mokaterm.Abstractions.Containers.DockerNames.PathProblem(root, "folder"));
		}

		string[] arguments =
		[
			depth.ToString(CultureInfo.InvariantCulture),
			SearchSecondsPerRoot.ToString(CultureInfo.InvariantCulture),
			.. searched,
		];

		return DockerCall.Script(FindScript, arguments) with
		{
			Timeout = TimeSpan.FromSeconds((SearchSecondsPerRoot * (searched.Count + 1)) + 10),
			OutputLimitBytes = 1024 * 1024,
		};
	}

	/// <summary>
	/// The files a search printed, one per line: absolute, each once, and only names compose reads by default. A path
	/// with a control character in it is dropped, since nothing can be passed on to compose for it.
	/// </summary>
	public static IReadOnlyList<string> ParseFoundFiles(string output)
	{
		ArgumentNullException.ThrowIfNull(output);
		return [.. output.Split('\n')
			.Select(line => line.TrimEnd('\r'))
			.Where(line => line.StartsWith('/') && !line.Any(char.IsControl) && ComposeFileNames.Contains(DockerHostPaths.NameOf(line), StringComparer.Ordinal))
			.Distinct(StringComparer.Ordinal)
			.Order(StringComparer.Ordinal)
			.Take(MaxFound)];
	}
}
