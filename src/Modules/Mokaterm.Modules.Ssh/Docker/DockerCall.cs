using System.Text;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// One run of the docker command line: the program, its words in order, and the folder it runs in. Built only by
/// <see cref="DockerCommands"/> and <see cref="ComposeCommands"/>, which validate every name first; the words are quoted
/// on the way out by <see cref="DockerCommandLine"/>.
/// </summary>
internal sealed record DockerCall
{
	/// <summary>What a listing or a quick change may take before it counts as hung.</summary>
	public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

	/// <summary>What a change that waits on containers (stop, restart, down) may take.</summary>
	public static readonly TimeSpan ChangeTimeout = TimeSpan.FromMinutes(3);

	/// <summary>What a pull or a build may take: images come over the network, builds run whatever the file says.</summary>
	public static readonly TimeSpan LongTimeout = TimeSpan.FromMinutes(20);

	/// <summary>How much of each stream a listing keeps. A host with a thousand containers prints about 2 MB.</summary>
	public const int ListingOutputBytes = 8 * 1024 * 1024;

	/// <summary>How much of each stream a change keeps for the output box.</summary>
	public const int ChangeOutputBytes = 512 * 1024;

	private static readonly IReadOnlySet<int> NoMasks = new HashSet<int>();

	public required DockerProgram Program { get; init; }

	public required IReadOnlyList<string> Arguments { get; init; }

	/// <summary>The folder the command runs in, for compose; null runs it wherever the login shell starts.</summary>
	public string? WorkingDirectory { get; init; }

	public TimeSpan Timeout { get; init; } = DefaultTimeout;

	public int OutputLimitBytes { get; init; } = ListingOutputBytes;

	/// <summary>
	/// stdout is bytes rather than text, such as a file read out of a container: it is kept as it came in
	/// <see cref="DockerOutput.RawOutput"/> and never decoded into <see cref="DockerOutput.Output"/>.
	/// </summary>
	public bool BinaryOutput { get; init; }

	/// <summary>
	/// The arguments that are <c>NAME=value</c> with a secret value, such as <c>--env</c> for a new container: what is
	/// shown masks the value after the first <c>=</c>, and the command runs with it as typed.
	/// </summary>
	public IReadOnlySet<int> MaskedArguments { get; init; } = NoMasks;

	/// <summary>The words that start the command: <c>docker</c>, <c>docker compose</c>, <c>docker-compose</c> or <c>sh -c</c>.</summary>
	public IReadOnlyList<string> ProgramWords => Program switch
	{
		DockerProgram.ComposePlugin => ["docker", "compose"],
		DockerProgram.ComposeStandalone => ["docker-compose"],
		DockerProgram.Shell => ["sh", "-c"],
		_ => ["docker"],
	};

	/// <summary>A script of the module's own: <paramref name="arguments"/> reach it as <c>$1</c> and on.</summary>
	public static DockerCall Script(string script, IEnumerable<string> arguments)
	{
		ArgumentNullException.ThrowIfNull(script);
		ArgumentNullException.ThrowIfNull(arguments);

		// The second word is $0, which sh names itself by in its messages.
		return new DockerCall { Program = DockerProgram.Shell, Arguments = [script, "sh", .. arguments] };
	}

	/// <summary>
	/// The command as a person would type it, which is also exactly what runs, apart from the values of
	/// <see cref="MaskedArguments"/>.
	/// </summary>
	public string Display => DockerCommandLine.Build(this, masked: true);

	/// <summary>The same words for a change: a longer wait and a smaller output cap.</summary>
	public DockerCall AsChange(TimeSpan? timeout = null) => this with
	{
		Timeout = timeout ?? ChangeTimeout,
		OutputLimitBytes = ChangeOutputBytes,
	};

	// A command typed into the run box, or an env file path, is part of the line, so the line itself is not printed.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Program = ").Append(Program).Append(", Arguments = ").Append(Arguments.Count).Append(", Timeout = ").Append(Timeout);
		return true;
	}
}
