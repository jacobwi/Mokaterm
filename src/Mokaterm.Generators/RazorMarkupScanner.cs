using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Mokaterm.Generators;

/// <summary>
/// Reads the <c>SettingsRow</c> and <c>SettingsSection</c> tags out of Razor markup. It is not a Razor parser: it
/// knows enough to step over comments, quoted attribute values and the C# expressions inside them, which is all a
/// settings page puts in its rows. A label or description that holds an expression is computed, so it is left out
/// rather than guessed at.
/// </summary>
internal static class RazorMarkupScanner
{
	private const string RowTag = "SettingsRow";
	private const string SectionTag = "SettingsSection";

	private static readonly Regex NamespaceDirective = new(@"^[ \t]*@namespace[ \t]+([A-Za-z_][\w.]*)", RegexOptions.Multiline | RegexOptions.CultureInvariant);
	private static readonly Regex TypeParamDirective = new(@"^[ \t]*@typeparam\b", RegexOptions.Multiline | RegexOptions.CultureInvariant);
	private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);

	public static RazorFile Scan(string path, string text, CancellationToken cancellationToken)
	{
		Match directive = NamespaceDirective.Match(text);
		string? @namespace = directive.Success ? directive.Groups[1].Value : null;
		bool generic = TypeParamDirective.IsMatch(text);

		// Most components hold no settings row at all; only those are walked tag by tag.
		ImmutableArray<SettingsRowInfo> rows = text.IndexOf(RowTag, StringComparison.Ordinal) < 0
			? ImmutableArray<SettingsRowInfo>.Empty
			: ReadRows(text, cancellationToken);

		return new RazorFile(path, @namespace, generic, new EquatableArray<SettingsRowInfo>(rows));
	}

	private static ImmutableArray<SettingsRowInfo> ReadRows(string text, CancellationToken cancellationToken)
	{
		ImmutableArray<SettingsRowInfo>.Builder rows = ImmutableArray.CreateBuilder<SettingsRowInfo>();
		List<string?> sections = [];
		int index = 0;
		while (index < text.Length)
		{
			cancellationToken.ThrowIfCancellationRequested();
			char current = text[index];
			if (current == '@' && At(text, index + 1, '*'))
			{
				index = SkipPast(text, index + 2, "*@");
				continue;
			}

			if (current != '<')
			{
				index++;
				continue;
			}

			if (string.CompareOrdinal(text, index, "<!--", 0, 4) == 0)
			{
				index = SkipPast(text, index + 4, "-->");
				continue;
			}

			bool closing = At(text, index + 1, '/');
			int nameStart = index + (closing ? 2 : 1);
			int nameEnd = ReadTagName(text, nameStart);
			string tag = SimpleName(text.Substring(nameStart, nameEnd - nameStart));
			if (tag != RowTag && tag != SectionTag)
			{
				index = Math.Max(nameEnd, index + 1);
				continue;
			}

			if (closing)
			{
				if (tag == SectionTag && sections.Count > 0)
				{
					sections.RemoveAt(sections.Count - 1);
				}

				index = nameEnd;
				continue;
			}

			TagAttributes attributes = ReadAttributes(text, nameEnd);
			index = attributes.End;
			if (tag == SectionTag)
			{
				if (!attributes.SelfClosing)
				{
					sections.Add(attributes.Literal("Title"));
				}

				continue;
			}

			// Searchable="false" is a row that lists what is loaded or says something, not a setting to find.
			if (string.Equals(attributes.Literal("Searchable"), "false", StringComparison.OrdinalIgnoreCase)
				|| attributes.Literal("Label") is not { Length: > 0 } label)
			{
				continue;
			}

			string? section = sections.Count > 0 ? sections[sections.Count - 1] : null;
			string? description = attributes.Literal("Description");
			rows.Add(new SettingsRowInfo(section, label, description is { Length: > 0 } ? description : null));
		}

		return rows.ToImmutable();
	}

	private static TagAttributes ReadAttributes(string text, int index)
	{
		Dictionary<string, string?> literals = new(StringComparer.Ordinal);
		while (true)
		{
			index = SkipWhitespace(text, index);
			if (index >= text.Length)
			{
				return new TagAttributes(literals, SelfClosing: false, text.Length);
			}

			if (text[index] == '>')
			{
				return new TagAttributes(literals, SelfClosing: false, index + 1);
			}

			if (text[index] == '/' && At(text, index + 1, '>'))
			{
				return new TagAttributes(literals, SelfClosing: true, index + 2);
			}

			int nameStart = index;
			while (index < text.Length
				&& !char.IsWhiteSpace(text[index])
				&& text[index] is not ('=' or '>')
				&& !(text[index] == '/' && At(text, index + 1, '>')))
			{
				index++;
			}

			if (index == nameStart)
			{
				index++;
				continue;
			}

			string name = text.Substring(nameStart, index - nameStart);
			int afterName = SkipWhitespace(text, index);
			if (afterName >= text.Length || text[afterName] != '=')
			{
				// A bare attribute such as Stacked: true, and nothing the search reads.
				continue;
			}

			index = SkipWhitespace(text, afterName + 1);
			if (index < text.Length && text[index] is '"' or '\'')
			{
				(string? value, int end) = ReadQuotedValue(text, index);
				literals[name] = value;
				index = end;
			}
			else if (index < text.Length && text[index] == '@')
			{
				literals[name] = null;
				index = SkipExpression(text, index + 1);
			}
			else
			{
				int valueStart = index;
				while (index < text.Length && !char.IsWhiteSpace(text[index]) && text[index] != '>' && !(text[index] == '/' && At(text, index + 1, '>')))
				{
					index++;
				}

				literals[name] = Clean(text.Substring(valueStart, index - valueStart));
			}
		}
	}

	/// <summary>The value between the quotes, or null when an expression is part of it.</summary>
	private static (string? Value, int End) ReadQuotedValue(string text, int index)
	{
		char quote = text[index];
		StringBuilder value = new();
		bool literal = true;
		int position = index + 1;
		while (position < text.Length)
		{
			char current = text[position];
			if (current == quote)
			{
				return (literal ? Clean(value.ToString()) : null, position + 1);
			}

			if (current != '@')
			{
				value.Append(current);
				position++;
				continue;
			}

			// Razor reads "@@" as one "@", and an "@" after a letter or digit as part of an address (user@host).
			if (At(text, position + 1, '@'))
			{
				value.Append('@');
				position += 2;
			}
			else if (position > index + 1 && char.IsLetterOrDigit(text[position - 1]))
			{
				value.Append('@');
				position++;
			}
			else if (position + 1 < text.Length && (text[position + 1] == '(' || IsIdentifierStart(text[position + 1])))
			{
				literal = false;
				position = SkipExpression(text, position + 1);
			}
			else
			{
				value.Append('@');
				position++;
			}
		}

		return (null, text.Length);
	}

	/// <summary>Steps over <c>(...)</c> or <c>Name.Member(...)[...]</c>, the two forms an expression after "@" takes.</summary>
	private static int SkipExpression(string text, int index)
	{
		if (At(text, index, '('))
		{
			return SkipBalanced(text, index);
		}

		index = SkipIdentifier(text, index);
		while (index < text.Length)
		{
			char current = text[index];
			if (current is '(' or '[')
			{
				index = SkipBalanced(text, index);
			}
			else if (current == '.' && index + 1 < text.Length && IsIdentifierStart(text[index + 1]))
			{
				index = SkipIdentifier(text, index + 1);
			}
			else if (current is '?' or '!' && At(text, index + 1, '.') && index + 2 < text.Length && IsIdentifierStart(text[index + 2]))
			{
				index = SkipIdentifier(text, index + 2);
			}
			else
			{
				break;
			}
		}

		return index;
	}

	/// <summary>From an opening bracket to just past its partner, stepping over C# strings and characters on the way.</summary>
	private static int SkipBalanced(string text, int index)
	{
		int depth = 0;
		while (index < text.Length)
		{
			char current = text[index];
			switch (current)
			{
				case '(' or '[' or '{':
					depth++;
					index++;
					break;
				case ')' or ']' or '}':
					depth--;
					index++;
					if (depth <= 0)
					{
						return index;
					}

					break;
				case '"':
					index = SkipString(text, index, verbatim: false, interpolated: false);
					break;
				case '\'':
					index = SkipCharacter(text, index);
					break;
				case '@' or '$' when IsStringPrefix(text, index, out int quote, out bool verbatim, out bool interpolated):
					index = SkipString(text, quote, verbatim, interpolated);
					break;
				default:
					index++;
					break;
			}
		}

		return index;
	}

	private static bool IsStringPrefix(string text, int index, out int quote, out bool verbatim, out bool interpolated)
	{
		verbatim = false;
		interpolated = false;
		quote = index;
		while (quote < text.Length && text[quote] is '@' or '$' && quote - index < 2)
		{
			verbatim |= text[quote] == '@';
			interpolated |= text[quote] == '$';
			quote++;
		}

		return quote > index && At(text, quote, '"');
	}

	private static int SkipString(string text, int quote, bool verbatim, bool interpolated)
	{
		int index = quote + 1;
		while (index < text.Length)
		{
			char current = text[index];
			if (current == '\\' && !verbatim)
			{
				index += 2;
			}
			else if (current == '"')
			{
				if (verbatim && At(text, index + 1, '"'))
				{
					index += 2;
					continue;
				}

				return index + 1;
			}
			else if (current == '{' && interpolated)
			{
				index = At(text, index + 1, '{') ? index + 2 : SkipBalanced(text, index);
			}
			else
			{
				index++;
			}
		}

		return index;
	}

	private static int SkipCharacter(string text, int index)
	{
		int position = index + 1;
		while (position < text.Length && text[position] != '\'')
		{
			position += text[position] == '\\' ? 2 : 1;
		}

		return Math.Min(position + 1, text.Length);
	}

	private static int SkipIdentifier(string text, int index)
	{
		while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] == '_'))
		{
			index++;
		}

		return index;
	}

	private static int ReadTagName(string text, int index)
	{
		while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] is '_' or '.' or '-' or ':'))
		{
			index++;
		}

		return index;
	}

	private static int SkipWhitespace(string text, int index)
	{
		while (index < text.Length && char.IsWhiteSpace(text[index]))
		{
			index++;
		}

		return index;
	}

	private static int SkipPast(string text, int index, string end)
	{
		int found = text.IndexOf(end, index, StringComparison.Ordinal);
		return found < 0 ? text.Length : found + end.Length;
	}

	/// <summary>A fully qualified tag (<c>Components.SettingsRow</c>) is the same component.</summary>
	private static string SimpleName(string name)
	{
		int dot = name.LastIndexOf('.');
		return dot < 0 ? name : name.Substring(dot + 1);
	}

	/// <summary>The text a page shows: entities decoded, and runs of whitespace (a value wrapped over lines) as one space.</summary>
	private static string Clean(string value)
	{
		string decoded = value.IndexOf('&') < 0 ? value : WebUtility.HtmlDecode(value);
		return Whitespace.Replace(decoded, " ").Trim();
	}

	private static bool At(string text, int index, char expected) => index < text.Length && text[index] == expected;

	private static bool IsIdentifierStart(char value) => char.IsLetter(value) || value == '_';

	private readonly record struct TagAttributes(Dictionary<string, string?> Literals, bool SelfClosing, int End)
	{
		/// <summary>The attribute's literal text, or null when it is missing or computed.</summary>
		public string? Literal(string name) => Literals.TryGetValue(name, out string? value) ? value : null;
	}
}
