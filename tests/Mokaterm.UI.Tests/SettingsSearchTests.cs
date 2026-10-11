using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Settings.Extensions;
using Mokaterm.UI.Shell;

namespace Mokaterm.UI.Tests;

public sealed class SettingsSearchTests
{
	// The search that started this: the row is called Right-click and nothing on the page said "right click".
	[Fact]
	public void Find_RightClick_FindsTheTerminalRow()
	{
		IReadOnlyList<SettingsSearchResult> results = SettingsSearch.Find(BuiltInPages(), "right click", SettingsSearchIndex.For);

		SettingsSearchResult row = Assert.Single(results, result => result.Row?.Label == "Right-click");
		Assert.Equal("terminal", row.Page.Id);
		Assert.Equal("right-click", row.Anchor);
	}

	[Fact]
	public void Find_PutsEachPageBeforeItsRows()
	{
		IReadOnlyList<SettingsSearchResult> results = SettingsSearch.Find(BuiltInPages(), "right click", SettingsSearchIndex.For);

		int page = IndexOf(results, result => result.Row is null && result.Page.Id == "terminal");
		int row = IndexOf(results, result => result.Row?.Label == "Right-click");
		Assert.True(page < row);
	}

	[Fact]
	public void Find_ARowMayTakeAWordFromItsPageTitle()
	{
		IReadOnlyList<SettingsSearchResult> results = SettingsSearch.Find(BuiltInPages(), "terminal font size", SettingsSearchIndex.For);

		Assert.Contains(results, result => result.Page.Id == "terminal" && result.Row?.Label == "Font size");
		Assert.DoesNotContain(results, result => result.Page.Id == "appearance" && result.Row is not null);
	}

	[Fact]
	public void Find_EveryWordHasToBeFound()
	{
		Assert.Empty(SettingsSearch.Find(BuiltInPages(), "right click xyzzy", SettingsSearchIndex.For));
		Assert.Empty(SettingsSearch.Find(BuiltInPages(), "  ", SettingsSearchIndex.For));
	}

	[Fact]
	public void Find_APageFoundByItsKeywords_ComesWithoutRows()
	{
		IReadOnlyList<SettingsSearchResult> results = SettingsSearch.Find(BuiltInPages(), "fingerprints", SettingsSearchIndex.For);

		SettingsSearchResult page = Assert.Single(results);
		Assert.Equal("known-hosts", page.Page.Id);
		Assert.Null(page.Anchor);
	}

	[Fact]
	public void Find_RowsNamedByTheWordsComeBeforeRowsThatOnlyMentionThem()
	{
		SettingsPageDescriptor page = Page("page", "Page");
		IReadOnlyList<SettingsSearchResult> results = SettingsSearch.Find([page], "proxy", _ =>
		[
			new SettingsSearchEntry(null, "Timeout", "How long a proxy may take."),
			new SettingsSearchEntry(null, "Proxy", null),
		]);

		Assert.Equal(new string?[] { null, "Proxy", "Timeout" }, results.Select(result => result.Row?.Label));
	}

	[Fact]
	public void Find_ARowDrawnTwiceUnderOneLabel_IsOneResult()
	{
		SettingsPageDescriptor page = Page("updates", "Updates");
		IReadOnlyList<SettingsSearchResult> results = SettingsSearch.Find([page], "download", _ =>
		[
			new SettingsSearchEntry("Version", "Download", "Fetches the new version."),
			new SettingsSearchEntry("Version", "Download", "Downloading the new version."),
		]);

		Assert.Equal("Fetches the new version.", Assert.Single(results, result => result.Row is not null).Row!.Description);
	}

	[Theory]
	[InlineData("Right-click", "right click")]
	[InlineData("  .NET   runtime ", "net runtime")]
	[InlineData("Auto-lock", "auto lock")]
	public void Fold_TurnsPunctuationIntoSpaces(string text, string expected) => Assert.Equal(expected, SettingsSearch.Fold(text));

	private static int IndexOf(IReadOnlyList<SettingsSearchResult> results, Func<SettingsSearchResult, bool> match)
	{
		for (int index = 0; index < results.Count; index++)
		{
			if (match(results[index]))
			{
				return index;
			}
		}

		return -1;
	}

	private static IReadOnlyList<SettingsPageDescriptor> BuiltInPages()
	{
		ServiceCollection services = new();
		services.AddMokatermSettingsPages();
		return [.. services.Select(service => service.ImplementationInstance).OfType<SettingsPageDescriptor>()];
	}

	private static SettingsPageDescriptor Page(string id, string title) => new()
	{
		Id = id,
		Title = title,
		Icon = MokatermIcons.Sliders,
		Component = typeof(ComponentBase),
	};
}
