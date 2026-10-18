using System.Globalization;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Docker;

/// <summary>
/// A limit the server keeps on a long docker command (a build, a push) by itself: the command runs under
/// <c>mokaterm_limit SECONDS</c> (<see cref="ShellTimeout"/>), so it stops on time even when nothing is left on this side
/// to stop it.
/// </summary>
/// <remarks>
/// Cancelling a call closes its exec channel after asking sshd to send TERM to the session. coreutils' timeout puts the
/// command in a process group of its own, which that TERM does not reach, so what ends docker then is the closed channel:
/// its next write fails and the client exits, and a BuildKit build (or the legacy builder's) is cancelled when its client
/// goes. A step that prints nothing for a long time keeps running until it prints or the limit here is reached.
/// </remarks>
internal static class DockerTimeLimit
{
	/// <summary>
	/// <c>$1</c> the seconds, then the command and its words. The limit's own probe comes first, then the command runs under
	/// it; a host whose timeout cannot be used runs it plainly, under the call's own limit.
	/// </summary>
	public static readonly string Script = (ShellTimeout.Function + "\nmokaterm_limit \"$@\"").ReplaceLineEndings("\n");

	/// <summary>
	/// How much longer this side waits than the server's limit, so the server's stop, and the exit code that says why,
	/// arrives first.
	/// </summary>
	private static readonly TimeSpan Grace = TimeSpan.FromMinutes(1);

	/// <summary>
	/// <paramref name="call"/> under a limit of <paramref name="limit"/> on the server: the same words after the seconds,
	/// so what the confirmation shows is still exactly what runs, the masks moved along with them. Long in the exec
	/// budget, whatever the call said.
	/// </summary>
	public static DockerCall Wrap(DockerCall call, TimeSpan limit)
	{
		ArgumentNullException.ThrowIfNull(call);
		if (call.Program == DockerProgram.Shell)
		{
			throw new ArgumentException("Only a docker or compose command can run under the limit.", nameof(call));
		}

		ArgumentOutOfRangeException.ThrowIfLessThan(limit, TimeSpan.FromSeconds(1));
		string seconds = ((long)Math.Ceiling(limit.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

		// DockerCall.Script puts the script and $0 first, then come the seconds and the program's own words.
		int shift = 3 + call.ProgramWords.Count;
		return DockerCall.Script(Script, [seconds, .. call.ProgramWords, .. call.Arguments]) with
		{
			WorkingDirectory = call.WorkingDirectory,
			Timeout = limit + Grace,
			RunsLong = true,
			OutputLimitBytes = call.OutputLimitBytes,
			MaskedArguments = call.MaskedArguments.Select(index => index + shift).ToHashSet(),
			Input = call.Input,
			OnOutput = call.OnOutput,
		};
	}

	/// <summary>The docker or compose call inside a <see cref="Wrap"/>ped one, or null for any other call.</summary>
	public static DockerCall? Unwrap(DockerCall call)
	{
		ArgumentNullException.ThrowIfNull(call);
		IReadOnlyList<string> words = call.Arguments;
		if (call.Program != DockerProgram.Shell || words.Count < 4 || !string.Equals(words[0], Script, StringComparison.Ordinal))
		{
			return null;
		}

		(DockerProgram program, int start) = words switch
		{
			[_, _, _, "docker", "compose", ..] => (DockerProgram.ComposePlugin, 5),
			[_, _, _, "docker-compose", ..] => (DockerProgram.ComposeStandalone, 4),
			[_, _, _, "docker", ..] => (DockerProgram.Docker, 4),
			_ => (DockerProgram.Shell, -1),
		};

		return start < 0 ? null : new DockerCall
		{
			Program = program,
			Arguments = [.. words.Skip(start)],
			WorkingDirectory = call.WorkingDirectory,
			MaskedArguments = call.MaskedArguments.Where(index => index >= start).Select(index => index - start).ToHashSet(),
		};
	}

	/// <summary>The limit a <see cref="Wrap"/>ped call runs under, or null for any other call.</summary>
	public static TimeSpan? LimitOf(DockerCall call)
	{
		ArgumentNullException.ThrowIfNull(call);
		return Unwrap(call) is not null && long.TryParse(call.Arguments[2], NumberStyles.None, CultureInfo.InvariantCulture, out long seconds)
			? TimeSpan.FromSeconds(seconds)
			: null;
	}

	/// <summary>True when a call that ran under the limit ended because its time ran out.</summary>
	public static bool RanOut(int? exitCode) => exitCode is { } code && ShellTimeout.RanOut(code);

	/// <summary>The limit as the user reads it: <c>2 h</c>, <c>45 min</c>.</summary>
	public static string Describe(TimeSpan limit) => limit.TotalHours >= 1 && limit.TotalMinutes % 60 == 0
		? string.Create(CultureInfo.CurrentCulture, $"{limit.TotalHours:0} h")
		: string.Create(CultureInfo.CurrentCulture, $"{limit.TotalMinutes:0} min");
}
