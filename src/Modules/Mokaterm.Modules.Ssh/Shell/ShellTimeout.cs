namespace Mokaterm.Modules.Ssh.Shell;

/// <summary>
/// The one way a remote script puts a limit of its own on a slow tool: <c>mokaterm_limit SECONDS COMMAND...</c> runs the
/// command under <c>timeout</c> where that works and plainly where it does not.
/// </summary>
/// <remarks>
/// coreutils' timeout and BusyBox's since 1.30 take the seconds first. An older BusyBox wants <c>-t</c> and fails the
/// probe, as does a system with no timeout at all, so the tool then runs under the call's own time limit alone. Testing
/// for the command alone is not enough: the old BusyBox one exists and would refuse every call.
/// </remarks>
internal static class ShellTimeout
{
	/// <summary>Defines <c>mokaterm_limit</c>. Put it at the top of a script, before the first use.</summary>
	public const string Function =
		"if timeout 1 true >/dev/null 2>&1; then mokaterm_timeout=1; else mokaterm_timeout=; fi\n" +
		"mokaterm_limit() { mokaterm_seconds=$1; shift; if [ -n \"$mokaterm_timeout\" ]; then timeout \"$mokaterm_seconds\" \"$@\"; else \"$@\"; fi; }";

	/// <summary>The exit codes a limited command ends with when its time ran out: 124 from timeout, 143 when it was killed by TERM.</summary>
	public static bool RanOut(int exitCode) => exitCode is 124 or 143;
}
