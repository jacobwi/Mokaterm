using System.Globalization;
using System.Text;

namespace Mokaterm.Abstractions.Commands;

/// <summary>
/// A command read for placeholders, the one rule for every place a command is typed or run from. <c>{{name}}</c> and
/// <c>{{name:default}}</c> are asked for first, a name being letters, digits, <c>_</c>, <c>-</c> and <c>.</c> that does
/// not start with <c>-</c> or <c>.</c>, and <c>{{{{</c> types a literal <c>{{</c>. Anything else between double braces
/// stays as typed, so Go templates (<c>docker ps --format '{{.Names}}'</c>, <c>{{json .}}</c>, <c>{{range .Mounts}}</c>
/// with the <c>{{end}}</c>, <c>{{else}}</c>, <c>{{break}}</c> and <c>{{continue}}</c> that close or steer them) and
/// Jinja written with spaces (<c>{{ item }}</c>) keep working. Values go in exactly as typed, never quoted.
/// </summary>
public sealed class CommandTemplate
{
	/// <summary>The most values one command can ask for, so a prompt always fits on screen.</summary>
	public const int MaxPlaceholders = 20;

	private const string Open = "{{";
	private const string Close = "}}";
	private const string EscapedOpen = "{{{{";

	// What a Go template (and Handlebars) writes alone between braces. A placeholder with one of these names would turn
	// the end of every range or if block into a question, so these stay as typed unless they carry a default.
	private static readonly HashSet<string> TemplateWords = new(StringComparer.Ordinal) { "end", "else", "break", "continue" };

	private readonly Dictionary<string, CommandPlaceholder> _byName;

	private CommandTemplate(string text, IReadOnlyList<CommandTemplatePart> parts, List<CommandPlaceholder> placeholders, string? problem)
	{
		Text = text;
		Parts = parts;
		Placeholders = placeholders;
		Problem = problem;
		_byName = placeholders.ToDictionary(placeholder => placeholder.Name, StringComparer.Ordinal);
	}

	/// <summary>The command as written, placeholders and all.</summary>
	public string Text { get; }

	/// <summary>Literal text and placeholders in order. Literal text has <c>{{{{</c> already read as <c>{{</c>.</summary>
	public IReadOnlyList<CommandTemplatePart> Parts { get; }

	/// <summary>Each name once, in the order it first appears, with the default it was given anywhere.</summary>
	public IReadOnlyList<CommandPlaceholder> Placeholders { get; }

	public bool HasPlaceholders => Placeholders.Count > 0;

	/// <summary>
	/// Why the command cannot be saved, worded for the user, or null when it is well formed: a placeholder with no name,
	/// one that is never closed, one name given two different defaults, a default with a control character, or more than
	/// <see cref="MaxPlaceholders"/> names. Where a command is used anyway, a broken placeholder stays as typed.
	/// </summary>
	public string? Problem { get; }

	/// <summary>Reads <paramref name="text"/> for placeholders. Never throws; what is wrong ends up in <see cref="Problem"/>.</summary>
	public static CommandTemplate Parse(string? text)
	{
		string source = text ?? "";
		List<CommandTemplatePart> parts = [];
		List<CommandPlaceholder> placeholders = [];
		StringBuilder literal = new();
		string? problem = null;
		int index = 0;
		while (index < source.Length)
		{
			int open = source.IndexOf(Open, index, StringComparison.Ordinal);
			if (open < 0)
			{
				literal.Append(source, index, source.Length - index);
				break;
			}

			literal.Append(source, index, open - index);
			Token token = Read(source, open);
			if (token.Kind != TokenKind.Placeholder)
			{
				// A broken placeholder is typed as it stands, like anything else that is not ours.
				literal.Append(Open);
				problem ??= token.Problem;
				index = open + token.Length;
				continue;
			}

			if (literal.Length > 0)
			{
				parts.Add(new CommandTemplatePart { Text = literal.ToString() });
				literal.Clear();
			}

			parts.Add(new CommandTemplatePart { Text = source.Substring(open, token.Length), Name = token.Name, Default = token.Default });

			// Every name is listed even after a problem, since a command that is used anyway still asks for all of them.
			string? added = Add(placeholders, token.Name!, token.Default);
			problem ??= added;
			index = open + token.Length;
		}

		if (literal.Length > 0)
		{
			parts.Add(new CommandTemplatePart { Text = literal.ToString() });
		}

		if (problem is null && placeholders.Count > MaxPlaceholders)
		{
			problem = string.Create(CultureInfo.InvariantCulture, $"A command can ask for at most {MaxPlaceholders} values.");
		}

		return new CommandTemplate(source, parts, placeholders, problem);
	}

	/// <summary>
	/// <paramref name="text"/> written so that it types exactly as it is: every <c>{{</c> that would be read as a
	/// placeholder, a broken one or an escape becomes <c>{{{{</c>. Braces that are not ours are left alone, so a Go
	/// template still reads as it did. For a command saved from a terminal, which already ran as it stands.
	/// </summary>
	public static string EscapeText(string? text)
	{
		string source = text ?? "";
		StringBuilder escaped = new(source.Length);
		int index = 0;
		while (index < source.Length)
		{
			int open = source.IndexOf(Open, index, StringComparison.Ordinal);
			if (open < 0)
			{
				escaped.Append(source, index, source.Length - index);
				break;
			}

			escaped.Append(source, index, open - index);

			// Parse reads a doubled opener here and carries on right after the original pair, the way this loop does, so
			// both walk the rest of the text in step and every later pair is judged on the same characters.
			escaped.Append(Read(source, open).Kind == TokenKind.Literal ? Open : EscapedOpen);
			index = open + Open.Length;
		}

		return escaped.ToString();
	}

	/// <summary>
	/// Why <paramref name="value"/> cannot go into a command, or null when it can. A value is one line: a line break
	/// would run the command early, and a tab or an escape would reach the shell as a key rather than as text.
	/// </summary>
	public static string? ValueProblem(string? value) =>
		value is not null && value.Any(char.IsControl)
			? "A value cannot hold a line break, a tab or another control character."
			: null;

	/// <summary>The default of <paramref name="name"/>, or null when it has none or is not a placeholder here.</summary>
	public string? DefaultOf(string name) => _byName.TryGetValue(name, out CommandPlaceholder? placeholder) ? placeholder.Default : null;

	/// <summary>
	/// The command with every placeholder replaced by its value, exactly as typed, or by its default when
	/// <paramref name="values"/> has none (an empty string without one). Throws <see cref="ArgumentException"/> for a
	/// value <see cref="ValueProblem"/> refuses, so no path can slip a line break into a terminal.
	/// </summary>
	public string Fill(IReadOnlyDictionary<string, string>? values)
	{
		StringBuilder filled = new(Text.Length);
		foreach (CommandTemplatePart part in Parts)
		{
			if (part.Name is not { } name)
			{
				filled.Append(part.Text);
				continue;
			}

			string value = values is not null && values.TryGetValue(name, out string? given) ? given : DefaultOf(name) ?? "";
			if (ValueProblem(value) is { } problem)
			{
				throw new ArgumentException(problem, nameof(values));
			}

			filled.Append(value);
		}

		return filled.ToString();
	}

	/// <summary>Lists <paramref name="name"/> once, and returns what is wrong with this use of it, or null.</summary>
	private static string? Add(List<CommandPlaceholder> placeholders, string name, string? defaultValue)
	{
		// Listed even with a bad default, so a command used anyway still asks for it and its field says what is wrong.
		string? problem = defaultValue is not null && ValueProblem(defaultValue) is not null
			? $"The default of {{{{{name}}}}} holds a tab or another control character."
			: null;

		int existing = placeholders.FindIndex(placeholder => string.Equals(placeholder.Name, name, StringComparison.Ordinal));
		if (existing < 0)
		{
			placeholders.Add(new CommandPlaceholder(name, defaultValue));
			return problem;
		}

		// The same name is asked for once, so it can only have one default; giving it at one use and not at another is fine.
		string? known = placeholders[existing].Default;
		if (defaultValue is null || known is null)
		{
			placeholders[existing] = new CommandPlaceholder(name, known ?? defaultValue);
			return problem;
		}

		return problem ?? (string.Equals(known, defaultValue, StringComparison.Ordinal)
			? null
			: $"{{{{{name}}}}} has two different defaults. Give it one.");
	}

	/// <summary>What the <c>{{</c> at <paramref name="open"/> starts.</summary>
	private static Token Read(string text, int open)
	{
		if (text.AsSpan(open).StartsWith(EscapedOpen, StringComparison.Ordinal))
		{
			return new Token(TokenKind.Escape, EscapedOpen.Length);
		}

		int start = open + Open.Length;
		if (start >= text.Length)
		{
			return Token.Literal;
		}

		char first = text[start];
		if (first is '}' or ':')
		{
			int closed = CloseOnLine(text, start);
			string excerpt = Excerpt(text, open, closed < 0 ? start + 1 : closed + Close.Length);
			return Token.Broken($"\"{excerpt}\" has no name. Write {{{{name}}}} or {{{{name:default}}}}, or {{{{{{{{ for a literal {{{{.");
		}

		if (!IsNameStart(first))
		{
			return Token.Literal;
		}

		int end = start + 1;
		while (end < text.Length && IsNamePart(text[end]))
		{
			end++;
		}

		string name = text[start..end];
		if (end < text.Length && text[end] == ':')
		{
			int close = CloseOnLine(text, end + 1);
			return close < 0
				? Token.Broken(Unclosed(Excerpt(text, open, LineEnd(text, end))))
				: new Token(TokenKind.Placeholder, close + Close.Length - open, name, text[(end + 1)..close]);
		}

		if (text.AsSpan(end).StartsWith(Close, StringComparison.Ordinal))
		{
			return TemplateWords.Contains(name) ? Token.Literal : new Token(TokenKind.Placeholder, end + Close.Length - open, name, null);
		}

		// A name that runs into the end of the line, or into a single brace, is a placeholder somebody did not finish.
		// Anything else after it (a space, a quote, a pipe) is another tool's syntax, such as {{json .}}.
		return end >= text.Length || text[end] is '}' or '\r' or '\n'
			? Token.Broken(Unclosed(Excerpt(text, open, end)))
			: Token.Literal;
	}

	private static bool IsNameStart(char character) => char.IsLetterOrDigit(character) || character == '_';

	private static bool IsNamePart(char character) => char.IsLetterOrDigit(character) || character is '_' or '-' or '.';

	/// <summary>Where the first <c>}}</c> from <paramref name="from"/> is, or -1 when the line ends first.</summary>
	private static int CloseOnLine(string text, int from)
	{
		for (int index = from; index < text.Length - 1; index++)
		{
			if (text[index] is '\r' or '\n')
			{
				return -1;
			}

			if (text[index] == '}' && text[index + 1] == '}')
			{
				return index;
			}
		}

		return -1;
	}

	/// <summary>Where the line holding <paramref name="from"/> ends: at its first line break, or with the text.</summary>
	private static int LineEnd(string text, int from)
	{
		int end = text.AsSpan(from).IndexOfAny('\r', '\n');
		return end < 0 ? text.Length : from + end;
	}

	private static string Unclosed(string excerpt) =>
		$"\"{excerpt}\" is not closed. End it with }}}}, or write {{{{{{{{ for a literal {{{{.";

	/// <summary>The broken placeholder as it was written, from its braces to <paramref name="end"/>, 32 characters at most.</summary>
	private static string Excerpt(string text, int open, int end)
	{
		const int MaxExcerpt = 32;
		return text[open..Math.Min(end, Math.Min(text.Length, open + MaxExcerpt))];
	}

	private enum TokenKind
	{
		/// <summary>Not ours: the two braces are typed as they are.</summary>
		Literal,

		/// <summary><c>{{{{</c>, typed as <c>{{</c>.</summary>
		Escape,

		Placeholder,

		/// <summary>Looks like ours but is not finished; typed as it is, and a problem for saving.</summary>
		Broken,
	}

	private readonly record struct Token(TokenKind Kind, int Length, string? Name = null, string? Default = null, string? Problem = null)
	{
		public static Token Literal => new(TokenKind.Literal, Open.Length);

		public static Token Broken(string problem) => new(TokenKind.Broken, Open.Length, Problem: problem);
	}
}
