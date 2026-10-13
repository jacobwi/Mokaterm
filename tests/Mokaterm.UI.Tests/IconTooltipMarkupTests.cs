using System.Text.RegularExpressions;

namespace Mokaterm.UI.Tests;

/// <summary>
/// An icon with no visible label is named by IconButton's tooltip. A <c>title</c> on one brings the browser's own tooltip
/// back beside it: a second late, unstyled box over the Moka one. This reads every .razor file under src the way Razor
/// does (attribute values may hold C# with quotes and brackets of their own) and fails on an icon-only MokaButton or an
/// IconButton that carries a title.
/// </summary>
public sealed partial class IconTooltipMarkupTests
{
	[Fact]
	public void NoIconOnlyButton_CarriesATitle()
	{
		string src = Path.Combine(RepositoryRoot(), "src");
		List<string> found = [];
		int read = 0;
		foreach (string file in Directory.EnumerateFiles(src, "*.razor", SearchOption.AllDirectories))
		{
			string relative = Path.GetRelativePath(src, file);
			if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
			{
				continue;
			}

			string text = File.ReadAllText(file);
			foreach (Match tag in ButtonStart().Matches(text))
			{
				(HashSet<string> attributes, bool iconOnly) = ReadButton(text, tag.Index + tag.Length, tag.Groups[1].Value);
				read++;
				if (attributes.Contains("title") && (iconOnly || tag.Groups[1].Value == "IconButton"))
				{
					found.Add($"{relative}:{LineOf(text, tag.Index)}");
				}
			}
		}

		// Hundreds of buttons, so an empty folder or a wrong root cannot pass for clean markup.
		Assert.True(read > 100, $"Only {read} buttons were read under {src}.");
		Assert.Empty(found);
	}

	[Theory]
	[InlineData("""<MokaButton StartIcon="X" title="Refresh" />""", true)]
	[InlineData("""<MokaButton StartIcon="X" aria-label="Refresh" />""", false)]
	[InlineData("""<MokaButton StartIcon="X" title="Save">Save</MokaButton>""", false)]
	[InlineData("""<MokaButton EndIcon="X" title="Menu">  </MokaButton>""", true)]
	[InlineData("""<MokaButton title="Plain" />""", false)]
	[InlineData("""<IconButton Icon="X" Label="Refresh" title="Refresh" />""", true)]
	[InlineData("""<MokaButton Label="@(on ? "Hide (Alt+H)" : "Show")" StartIcon="X" aria-label="@($"Edit {(a ? "x" : "y")} title=")" />""", false)]
	[InlineData("""<MokaButton Text="@count.ToString("N0", culture)" StartIcon="X" title="@Hint(a, "b")" />""", true)]
	public void TheReader_FindsTitlesOnIconOnlyButtons_AndNowhereElse(string markup, bool flagged)
	{
		Match tag = ButtonStart().Match(markup);
		(HashSet<string> attributes, bool iconOnly) = ReadButton(markup, tag.Index + tag.Length, tag.Groups[1].Value);

		Assert.Equal(flagged, attributes.Contains("title") && (iconOnly || tag.Groups[1].Value == "IconButton"));
	}

	private static (HashSet<string> Attributes, bool IconOnly) ReadButton(string text, int at, string name)
	{
		HashSet<string> attributes = new(StringComparer.Ordinal);
		bool selfClosing = false;
		int i = at;
		while (i < text.Length)
		{
			while (i < text.Length && char.IsWhiteSpace(text[i]))
			{
				i++;
			}

			if (i >= text.Length || text[i] == '>')
			{
				i++;
				break;
			}

			if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '>')
			{
				selfClosing = true;
				i += 2;
				break;
			}

			int start = i;
			while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('=' or '>' or '/'))
			{
				i++;
			}

			attributes.Add(text[start..i]);
			if (i < text.Length && text[i] == '=')
			{
				i++;
				i = text[i] == '"' ? SkipValue(text, i + 1) : SkipUnquoted(text, i);
			}
			else if (i == start)
			{
				// A stray character; step over it rather than loop on it.
				i++;
			}
		}

		bool hasIcon = attributes.Contains("StartIcon") || attributes.Contains("EndIcon") || attributes.Contains("Icon");
		bool noContent = selfClosing || EmptyUntilClose(text, i, name);
		return (attributes, hasIcon && noContent);
	}

	private static bool EmptyUntilClose(string text, int i, string name)
	{
		int close = text.IndexOf($"</{name}>", i, StringComparison.Ordinal);
		return close >= 0 && string.IsNullOrWhiteSpace(text[i..close]);
	}

	private static int SkipUnquoted(string text, int i)
	{
		while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '>')
		{
			i++;
		}

		return i;
	}

	// A quoted attribute value: text up to the closing quote, where @(...) and @name(...) hold C# whose own quotes and
	// brackets do not end the value.
	private static int SkipValue(string text, int i)
	{
		while (i < text.Length)
		{
			char c = text[i];
			if (c == '"')
			{
				return i + 1;
			}

			if (c == '@' && i + 1 < text.Length && text[i + 1] == '(')
			{
				i = SkipCode(text, i + 2, ')');
				continue;
			}

			if (c == '@')
			{
				i++;
				while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '.'))
				{
					i++;
				}

				while (i < text.Length && text[i] is '(' or '[')
				{
					i = SkipCode(text, i + 1, text[i] == '(' ? ')' : ']');
				}

				continue;
			}

			i++;
		}

		return i;
	}

	// C# up to the close bracket that matches the one before i, stepping over strings, interpolations and characters.
	private static int SkipCode(string text, int i, char close)
	{
		int depth = 0;
		while (i < text.Length)
		{
			char c = text[i];
			if (c == '"')
			{
				i = SkipString(text, i);
				continue;
			}

			if (c == '\'')
			{
				i += text[i + 1] == '\\' ? 4 : 3;
				continue;
			}

			if (c is '(' or '[' or '{')
			{
				depth++;
			}
			else if (c is ')' or ']' or '}')
			{
				if (depth == 0 && c == close)
				{
					return i + 1;
				}

				depth--;
			}

			i++;
		}

		return i;
	}

	private static int SkipString(string text, int i)
	{
		bool interpolated = i > 0 && text[i - 1] == '$';
		i++;
		while (i < text.Length)
		{
			char c = text[i];
			if (c == '\\')
			{
				i += 2;
				continue;
			}

			if (c == '"')
			{
				return i + 1;
			}

			if (interpolated && c == '{')
			{
				if (i + 1 < text.Length && text[i + 1] == '{')
				{
					i += 2;
					continue;
				}

				i = SkipCode(text, i + 1, '}');
				continue;
			}

			i++;
		}

		return i;
	}

	private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

	private static string RepositoryRoot()
	{
		for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
		{
			if (File.Exists(Path.Combine(folder.FullName, "Mokaterm.slnx")))
			{
				return folder.FullName;
			}
		}

		Assert.Fail("The tests are not running inside the repository, so there are no .razor files to read.");
		return "";
	}

	[GeneratedRegex("<(MokaButton|IconButton)\\b")]
	private static partial Regex ButtonStart();
}
