using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Sessions.Commands;

/// <summary>Turns what a command wrote into text for a monospace box: no escape sequences, one kind of line break.</summary>
internal static class CommandOutputDecoder
{
	public static CommandOutputText Decode(CapturedOutput captured)
	{
		ArgumentNullException.ThrowIfNull(captured);
		return captured.TotalBytes == 0
			? CommandOutputText.Empty
			: new CommandOutputText(TerminalTextFilter.Plain(captured.ToText()), captured.TotalBytes, captured.IsTruncated);
	}
}
