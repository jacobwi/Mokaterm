using System.Globalization;
using System.Text;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.Terminal.Triggers;

/// <summary>What a notice from a trigger says. The line comes from the server, so it is cleaned before it is shown.</summary>
internal static class TriggerNotice
{
	/// <summary>Characters of the matched line a notice shows.</summary>
	public const int MaxLineLength = 160;

	private const int MaxPatternLength = 60;

	/// <summary>The color a rule paints with says how serious its match is: red is an error, green a success.</summary>
	public static NoticeSeverity Severity(TriggerColor color) => color switch
	{
		TriggerColor.Red => NoticeSeverity.Error,
		TriggerColor.Orange or TriggerColor.Yellow => NoticeSeverity.Warning,
		TriggerColor.Green => NoticeSeverity.Success,
		_ => NoticeSeverity.Info,
	};

	/// <summary>Names the session the line came from, since a notice shows whichever tab is in front.</summary>
	public static string Title(string? session) =>
		string.IsNullOrWhiteSpace(session) ? "Trigger" : string.Create(CultureInfo.CurrentCulture, $"Trigger in {session.Trim()}");

	/// <summary>
	/// The matched line on one line of its own: control characters (escape sequences included) and the bidirectional
	/// controls that would reorder how it reads are dropped, runs of blanks shrink to one, and a long line is cut.
	/// </summary>
	public static string Line(string? line)
	{
		string text = OneLine(line);
		return text.Length == 0 ? "(an empty line)" : Cut(text, MaxLineLength);
	}

	/// <summary>Names the rule that matched by its pattern, which is the only name a rule has.</summary>
	public static string Rule(string? pattern)
	{
		string text = OneLine(pattern);
		return text.Length == 0 ? "Matched a trigger" : string.Create(CultureInfo.CurrentCulture, $"Matched \"{Cut(text, MaxPatternLength)}\"");
	}

	/// <summary>
	/// The system notification for a match: the session and the rule, and the line only when the user asked for it,
	/// because the system keeps its notifications after the app has gone and a server's line can hold a token. A click on
	/// it comes back with the ids alone.
	/// </summary>
	public static SystemNotification ForSystem(TriggerSource source, TerminalTrigger rule, string? line, bool showLine)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(rule);
		return new SystemNotification
		{
			Title = Title(source.Label),
			Message = Rule(rule.Pattern),
			Detail = showLine ? Line(line) : null,
			SessionId = source.SessionId,
			ConnectionId = source.ConnectionId,
		};
	}

	/// <summary>Why a rule stopped running in one terminal, and where to fix it.</summary>
	public static string Slow(string pattern) => string.Create(
		CultureInfo.CurrentCulture,
		$"\"{Cut(pattern, MaxPatternLength)}\" took too long on one line, so it is off in this terminal. Simplify it on the Triggers settings page.");

	// Control characters (escape sequences included) and the bidirectional controls that would reorder how it reads are
	// dropped, and runs of blanks shrink to one.
	private static string OneLine(string? line)
	{
		// The page sends a few hundred characters at most; this bounds the work if it ever sends more.
		ReadOnlySpan<char> source = (line ?? "").AsSpan();
		source = source[..Math.Min(source.Length, MaxLineLength * 4)];
		StringBuilder text = new(Math.Min(source.Length, MaxLineLength * 2));
		bool blank = false;
		foreach (char character in source)
		{
			if (char.IsControl(character) || char.IsWhiteSpace(character) || IsBidiControl(character))
			{
				blank = text.Length > 0;
				continue;
			}

			if (blank)
			{
				text.Append(' ');
				blank = false;
			}

			text.Append(character);
		}

		return text.ToString();
	}

	private static string Cut(string text, int length)
	{
		if (text.Length <= length)
		{
			return text;
		}

		int end = char.IsHighSurrogate(text[length - 1]) ? length - 1 : length;
		return string.Concat(text.AsSpan(0, end), "...");
	}

	private static bool IsBidiControl(char character) =>
		character is '\u061c' or '\u200e' or '\u200f' or (>= '\u202a' and <= '\u202e') or (>= '\u2066' and <= '\u2069');
}
