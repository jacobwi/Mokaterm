using System.Globalization;
using System.Text;

namespace Mokaterm.Modules.Local.Containers;

/// <summary>What a short command run on this computer printed and how it ended.</summary>
/// <param name="ExitCode">The exit code, or null when the command was stopped before it exited.</param>
/// <param name="Output">Standard output, up to the caller's limit.</param>
/// <param name="Error">Standard error, up to a limit of its own.</param>
/// <param name="TimedOut">True when the command ran out of time and was stopped.</param>
/// <param name="OutputCut">True when standard output went past the limit and the rest was dropped.</param>
internal sealed record LocalCommandResult(int? ExitCode, string Output, string Error, bool TimedOut, bool OutputCut)
{
	private bool PrintMembers(StringBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);

		// What a listing prints names machines and containers, which no log should carry.
		builder.Append(
			CultureInfo.InvariantCulture,
			$"ExitCode = {ExitCode}, Output = {Output.Length} characters, Error = {Error.Length} characters, TimedOut = {TimedOut}, OutputCut = {OutputCut}");
		return true;
	}
}
