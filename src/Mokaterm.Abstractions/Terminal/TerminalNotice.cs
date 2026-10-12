namespace Mokaterm.Abstractions.Terminal;

/// <summary>
/// A line the app writes into a session's terminal about the session itself ("connection closed", "waiting for the
/// server"): on a line of its own, dimmed and marked, so it never reads as the server's own output.
/// </summary>
public static class TerminalNotice
{
	private const char Escape = (char)0x1B;

	/// <summary>The notice ready for <see cref="ITerminalStream.WriteLocalAsync"/>, with control characters turned into spaces.</summary>
	public static string Format(string notice)
	{
		ArgumentNullException.ThrowIfNull(notice);
		string printable = string.Create(notice.Length, notice, static (span, source) =>
		{
			for (int i = 0; i < source.Length; i++)
			{
				span[i] = char.IsControl(source[i]) ? ' ' : source[i];
			}
		});

		return $"\r\n{Escape}[2m[mokaterm] {printable}{Escape}[0m\r\n";
	}
}
