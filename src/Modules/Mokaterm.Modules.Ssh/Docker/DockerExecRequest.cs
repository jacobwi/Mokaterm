using System.Text;
using System.Text.RegularExpressions;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.Credentials;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// One command run inside a container without a terminal: <c>docker exec [-u USER] [-w DIR] [-e KEY=VALUE]... NAME</c>
/// followed by the shell and the command, or by the command's own words. Nothing here is ever printed: the command and
/// the environment are where tokens go.
/// </summary>
internal sealed partial record DockerExecRequest
{
	/// <summary>How long a command may run before the manager stops waiting and closes its channel.</summary>
	public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

	/// <summary>How much of each stream is kept for the box.</summary>
	public const int OutputLimitBytes = 1024 * 1024;

	public required string Container { get; init; }

	public required string Command { get; init; }

	public DockerExecShell Shell { get; init; }

	/// <summary><c>name</c>, <c>uid</c> or either with <c>:group</c>; empty runs as the image's own user.</summary>
	public string User { get; init; } = "";

	/// <summary>An absolute path inside the container; empty keeps the image's working directory.</summary>
	public string WorkingDirectory { get; init; } = "";

	/// <summary><c>KEY=VALUE</c> pairs, one per entry.</summary>
	public IReadOnlyList<string> Environment { get; init; } = [];

	/// <summary>Why the request cannot run, or null when it can.</summary>
	public string? Problem()
	{
		if (DockerNames.ContainerProblem(Container) is { } container)
		{
			return container;
		}

		string command = Command.Trim();
		if (command.Length == 0)
		{
			return "Type a command to run.";
		}

		// A shell reads tabs and line breaks as part of its script; a word list has no use for them.
		if (command.Any(character => char.IsControl(character) && (Shell == DockerExecShell.None || character is not ('\t' or '\n' or '\r'))))
		{
			return Shell == DockerExecShell.None
				? "Without a shell the command has to be one line with no control characters."
				: "The command holds a control character other than a tab or a line break.";
		}

		if (Shell == DockerExecShell.None && DockerExecWords.Split(command) is { Problem: { } words })
		{
			return words;
		}

		if (User.Trim() is { Length: > 0 } user && !UserPattern().IsMatch(user))
		{
			return "The user is a name or a number, with an optional :group, such as www-data or 1000:1000.";
		}

		if (WorkingDirectory.Trim() is { Length: > 0 } directory && DockerNames.PathProblem(directory, "working directory") is { } path)
		{
			return path;
		}

		foreach (string variable in Environment)
		{
			int equals = variable.IndexOf('=', StringComparison.Ordinal);
			if (equals <= 0 || !VariablePattern().IsMatch(variable[..equals]))
			{
				return $"Each variable is NAME=value, with a name of letters, digits and '_'. This one is not: {DescribeName(variable)}.";
			}

			if (variable.Any(char.IsControl))
			{
				return $"The value of {variable[..equals]} holds a control character.";
			}
		}

		return null;
	}

	/// <summary>The words after <c>docker</c>.</summary>
	/// <exception cref="ArgumentException">The request breaks a rule; <see cref="Problem"/> says which.</exception>
	public IReadOnlyList<string> Arguments()
	{
		if (Problem() is { } problem)
		{
			throw new ArgumentException(problem);
		}

		List<string> words = ["exec"];
		if (User.Trim() is { Length: > 0 } user)
		{
			words.Add("-u");
			words.Add(user);
		}

		if (WorkingDirectory.Trim() is { Length: > 0 } directory)
		{
			words.Add("-w");
			words.Add(directory);
		}

		foreach (string variable in Environment)
		{
			words.Add("-e");
			words.Add(variable);
		}

		words.Add(Container.Trim());
		string command = Command.Trim().ReplaceLineEndings("\n");
		switch (Shell)
		{
			case DockerExecShell.None:
				words.AddRange(DockerExecWords.Split(command).Words);
				break;
			case DockerExecShell.Bash:
				words.AddRange(["bash", "-c", command]);
				break;
			default:
				words.AddRange(["sh", "-c", command]);
				break;
		}

		return words;
	}

	/// <summary>The call, with the box's own time limit and output cap.</summary>
	public DockerCall ToCall() => new()
	{
		Program = DockerProgram.Docker,
		Arguments = Arguments(),
		Timeout = Timeout,
		OutputLimitBytes = OutputLimitBytes,
	};

	/// <summary>The environment as the box shows it: one <c>NAME=value</c> per line, blank lines dropped.</summary>
	public static IReadOnlyList<string> ParseEnvironment(string? text) =>
		(text ?? "").ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

	private static string DescribeName(string variable)
	{
		int equals = variable.IndexOf('=', StringComparison.Ordinal);
		string name = equals < 0 ? variable : variable[..equals];
		return name.Length > 40 ? name[..40] + "..." : name;
	}

	[GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9_.-]{0,63}(:[A-Za-z0-9_][A-Za-z0-9_.-]{0,63})?\z", RegexOptions.CultureInvariant)]
	private static partial Regex UserPattern();

	[GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,127}\z", RegexOptions.CultureInvariant)]
	private static partial Regex VariablePattern();

	private bool PrintMembers(StringBuilder builder)
	{
		builder
			.Append("Container = ").Append(Container)
			.Append(", Command = ").Append(SecretText.Describe(Command))
			.Append(", Shell = ").Append(Shell)
			.Append(", Environment = ").Append(Environment.Count).Append(" variables");
		return true;
	}
}
