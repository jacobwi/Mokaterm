using System.Text.RegularExpressions;
using Moka.Red.Extensions;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Common.ToolPages;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The side menu every paged tool shares, by page id: pages in the order given, the current one marked, and one that
/// cannot be used here greyed with its reason rather than left out.
/// </summary>
public sealed partial class ToolNavigationTests
{
	[Fact]
	public async Task Entries_AreDrawnInOrder_TheCurrentOneMarked()
	{
		string html = await RenderAsync(
			[
				new ToolNavEntry("overview", "Overview", MokatermIcons.Server),
				new ToolNavEntry("services", "Services", MokatermIcons.Server),
				new ToolNavEntry("fail2ban", "Fail2ban", MokatermIcons.Shield),
			],
			current: "services");

		Assert.Equal(["Overview", "Services", "Fail2ban"], Labels(html));
		Match current = CurrentButton().Match(Tooltips.ControlFor(html, "Services"));
		Assert.True(current.Success);
		Assert.Contains("mt-toolnav-item--active", current.Value, StringComparison.Ordinal);
		Assert.DoesNotContain("title=", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task APageThatCannotBeUsed_StaysListed_GreyedWithItsReason()
	{
		string html = await RenderAsync(
			[
				new ToolNavEntry("overview", "Overview", MokatermIcons.Server),
				new ToolNavEntry("fail2ban", "Fail2ban", MokatermIcons.Shield, "fail2ban is not installed here."),
			],
			current: "overview");

		Assert.Contains("Fail2ban", Labels(html));
		Assert.Contains("mt-toolnav-item--off", Tooltips.ControlFor(html, "Fail2ban: fail2ban is not installed here."), StringComparison.Ordinal);
		Assert.Equal("right", Tooltips.PositionOf(html, "Fail2ban: fail2ban is not installed here."));
	}

	private static List<string> Labels(string html) => [.. Label().Matches(html).Select(match => match.Groups[1].Value)];

	private static Task<string> RenderAsync(IReadOnlyList<ToolNavEntry> entries, string current) =>
		StaticRender.RenderAsync<ToolNavigation>(
			services => services.AddMokaRed(),
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				[nameof(ToolNavigation.Entries)] = entries,
				[nameof(ToolNavigation.Current)] = current,
				[nameof(ToolNavigation.Label)] = "System pages",
			})
			.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

	[GeneratedRegex("<span[^>]*class=\"mt-toolnav-label\"[^>]*>([^<]*)</span>")]
	private static partial Regex Label();

	[GeneratedRegex("<button[^>]*aria-current=\"page\"[^>]*>")]
	private static partial Regex CurrentButton();
}
