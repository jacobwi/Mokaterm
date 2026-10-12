using System.Text;

namespace Mokaterm.Abstractions.Containers;

/// <summary>
/// One run of docker or compose that a Docker manager page asks for: the program, its words in order and the folder it
/// runs in. Every word is quoted on its way to the login shell, and through sudo it reaches the program as an argument,
/// never as script text. A word that names something (a container, an image, a volume, a project) passes
/// <see cref="DockerNames"/> first, because quoting cannot stop a name that starts with a dash from reading as an option.
/// </summary>
public sealed record DockerCommand
{
	public required DockerCommandProgram Program { get; init; }

	/// <summary>The words after the program, such as <c>ps</c>, <c>--all</c>, <c>--format</c>, <c>{{json .}}</c>.</summary>
	public required IReadOnlyList<string> Arguments { get; init; }

	/// <summary>The folder the command runs in, such as a compose project's; null runs it wherever the login starts.</summary>
	public string? WorkingDirectory { get; init; }

	/// <summary>
	/// A change rather than a read: it may wait longer, keeps less output, and the manager's health watch does not report
	/// what it did to a container.
	/// </summary>
	public bool IsChange { get; init; }

	/// <summary>How long it may run. Null takes the manager's own limit for a read or a change; longer than a pull may take is cut to that.</summary>
	public TimeSpan? Timeout { get; init; }

	/// <summary><c>docker</c> with <paramref name="arguments"/>.</summary>
	public static DockerCommand Docker(params IEnumerable<string> arguments)
	{
		ArgumentNullException.ThrowIfNull(arguments);
		return new DockerCommand { Program = DockerCommandProgram.Docker, Arguments = [.. arguments] };
	}

	/// <summary>Whichever compose the server has (<c>docker compose</c> or <c>docker-compose</c>) with <paramref name="arguments"/>.</summary>
	public static DockerCommand Compose(params IEnumerable<string> arguments)
	{
		ArgumentNullException.ThrowIfNull(arguments);
		return new DockerCommand { Program = DockerCommandProgram.Compose, Arguments = [.. arguments] };
	}

	/// <summary>The same words as a change, with <paramref name="timeout"/> when it needs longer than the manager gives one.</summary>
	public DockerCommand AsChange(TimeSpan? timeout = null) => this with { IsChange = true, Timeout = timeout ?? Timeout };

	// The words can carry a name the user typed or a value from a container's environment.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Program = ").Append(Program).Append(", Arguments = ").Append(Arguments.Count).Append(", IsChange = ").Append(IsChange);
		return true;
	}
}
