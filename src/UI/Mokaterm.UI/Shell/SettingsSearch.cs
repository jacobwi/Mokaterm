using System.Text;
using Mokaterm.UI.Common.Contributions;

namespace Mokaterm.UI.Shell;

/// <summary>
/// Finds settings by what their pages say: a page by its title, description and keywords, and a row by its label,
/// description and section. Every word typed has to be found, and a row may take a word from its page's title, so
/// "terminal font" finds Font family on the Terminal page. Words match anywhere and punctuation counts as a space, so
/// "right click" finds Right-click.
/// </summary>
internal static class SettingsSearch
{
	/// <summary>
	/// Each page that matched or holds a row that did, followed by those rows, in page order. Rows whose label holds
	/// every word come before rows found by their description.
	/// </summary>
	public static IReadOnlyList<SettingsSearchResult> Find(
		IEnumerable<SettingsPageDescriptor> pages,
		string query,
		Func<Type, IReadOnlyList<SettingsSearchEntry>> rowsOf)
	{
		ArgumentNullException.ThrowIfNull(pages);
		ArgumentNullException.ThrowIfNull(rowsOf);
		string[] words = Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (words.Length == 0)
		{
			return [];
		}

		List<SettingsSearchResult> results = [];
		foreach (SettingsPageDescriptor page in pages)
		{
			string title = Fold(page.Title);
			string pageText = Fold($"{page.Title} {page.Description} {page.Keywords}");
			bool pageMatches = words.All(word => Has(pageText, word));

			List<SettingsSearchEntry> byLabel = [];
			List<SettingsSearchEntry> byText = [];
			HashSet<string> anchors = new(StringComparer.Ordinal);
			foreach (SettingsSearchEntry row in rowsOf(page.Component))
			{
				// A row drawn differently in two states (Download, then its progress) is one place on the page.
				if (!anchors.Add(row.Anchor))
				{
					continue;
				}

				string label = Fold(row.Label);
				string text = Fold($"{row.Label} {row.Section} {row.Description}");
				if (!words.Any(word => Has(text, word)) || !words.All(word => Has(text, word) || Has(title, word)))
				{
					continue;
				}

				(words.All(word => Has(label, word) || Has(title, word)) ? byLabel : byText).Add(row);
			}

			if (pageMatches || byLabel.Count > 0 || byText.Count > 0)
			{
				results.Add(new SettingsSearchResult(page, null));
				results.AddRange(byLabel.Concat(byText).Select(row => new SettingsSearchResult(page, row)));
			}
		}

		return results;
	}

	private static bool Has(string text, string word) => text.Contains(word, StringComparison.Ordinal);

	/// <summary>Lower case, with every run of characters that are neither letters nor digits turned into one space.</summary>
	internal static string Fold(string? text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return "";
		}

		StringBuilder folded = new(text.Length);
		bool space = false;
		foreach (char character in text)
		{
			if (char.IsLetterOrDigit(character))
			{
				if (space && folded.Length > 0)
				{
					folded.Append(' ');
				}

				folded.Append(char.ToLowerInvariant(character));
				space = false;
			}
			else
			{
				space = true;
			}
		}

		return folded.ToString();
	}
}
