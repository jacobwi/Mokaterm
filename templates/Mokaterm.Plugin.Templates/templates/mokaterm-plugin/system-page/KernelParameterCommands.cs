using System.Text;
using System.Text.RegularExpressions;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.ServerAdmin;

namespace MokatermPlugin1;

/// <summary>
/// The script the Kernel parameters page runs and how its output is read. The names come from the plugin's settings,
/// so each one passes <see cref="NameProblem"/> and is then quoted before it goes into the script: the System view runs
/// a script exactly as written.
/// </summary>
internal static partial class KernelParameterCommands
{
	public const int MaxNameLength = 128;

	// One line per parameter: its name, a tab, then = and the value, or ! and why there is none. tr turns a value's own
	// tabs and line breaks into spaces, so a value never spans lines. The error redirection comes before the input one,
	// so a file that cannot be opened prints nothing of its own.
	private const string ReadFunction = """
		mokaterm_parameter() {
			if value=$(tr '\t\n' '  ' 2>/dev/null < "/proc/sys/$2"); then
				printf '%s\t=%s\n' "$1" "$value"
			elif [ -d "/proc/sys/$2" ]; then
				printf '%s\t!group\n' "$1"
			elif [ -e "/proc/sys/$2" ]; then
				printf '%s\t!unreadable\n' "$1"
			else
				printf '%s\t!missing\n' "$1"
			fi
		}
		""";

	/// <summary>Why <paramref name="name"/> is not a parameter name this page reads, or null: sysctl's dotted form, such as <c>vm.swappiness</c>.</summary>
	public static string? NameProblem(string? name) =>
		name is { Length: > 0 and <= MaxNameLength } && NamePattern().IsMatch(name)
			? null
			: $"\"{name}\" is not a parameter name: use sysctl's form, such as vm.swappiness, with letters, digits, dashes and underscores between the dots.";

	/// <summary>Reads every parameter in <paramref name="names"/> in one round trip; as root when <paramref name="asRoot"/>, since a few are root's alone.</summary>
	/// <exception cref="ArgumentException">A name is not one <see cref="NameProblem"/> takes.</exception>
	public static SystemScript Read(IReadOnlyList<string> names, bool asRoot)
	{
		ArgumentNullException.ThrowIfNull(names);
		StringBuilder body = new(ReadFunction);
		foreach (string name in names)
		{
			if (NameProblem(name) is { } problem)
			{
				throw new ArgumentException(problem, nameof(names));
			}

			body.Append('\n')
				.Append("mokaterm_parameter ")
				.Append(PosixShell.Quote(name))
				.Append(' ')
				.Append(PosixShell.Quote(name.Replace('.', '/')));
		}

		return new SystemScript
		{
			Name = "kernel-parameters.read",
			Body = body.ToString(),
			AsRoot = asRoot,
		};
	}

	/// <summary>
	/// What <see cref="Read"/> printed, as one entry per name in the order asked for. A name the output does not mention
	/// gets a problem rather than dropping out of the list.
	/// </summary>
	public static IReadOnlyList<KernelParameter> Parse(string output, IReadOnlyList<string> names, bool readAsRoot)
	{
		ArgumentNullException.ThrowIfNull(output);
		ArgumentNullException.ThrowIfNull(names);
		Dictionary<string, KernelParameter> read = new(StringComparer.Ordinal);
		foreach (string line in output.ReplaceLineEndings("\n").Split('\n'))
		{
			int tab = line.IndexOf('\t', StringComparison.Ordinal);
			if (tab <= 0)
			{
				continue;
			}

			string name = line[..tab];
			string answer = line[(tab + 1)..];
			read[name] = answer switch
			{
				['=', ..] => new KernelParameter(name, answer[1..].Trim(), null),
				"!unreadable" => new KernelParameter(name, null, readAsRoot
					? "Not readable, even as root: the kernel only takes writes to it."
					: "Not readable as this login. Root mode may read it."),
				"!group" => new KernelParameter(name, null, "This names a group of parameters; name one of them, such as net.ipv4.ip_forward."),
				"!missing" => new KernelParameter(name, null, "This kernel has no such parameter."),
				_ => new KernelParameter(name, null, "The answer for it could not be read."),
			};
		}

		return [.. names.Select(name => read.TryGetValue(name, out KernelParameter? parameter)
			? parameter
			: new KernelParameter(name, null, "No answer came back for it."))];
	}

	// \z rather than $, which would also match before a final line break.
	[GeneratedRegex(@"^[A-Za-z0-9_-]+([.][A-Za-z0-9_-]+)*\z", RegexOptions.CultureInvariant)]
	private static partial Regex NamePattern();
}
