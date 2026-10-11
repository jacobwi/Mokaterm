using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Moka.Red.Icons;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Interaction;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

/// <summary>The session toolbar as it renders: the layout's buttons, its lines, the dots on icons and the shortcuts in tooltips.</summary>
public sealed partial class SessionToolbarTests
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Grouped_DrawsTheGroupsWithLinesBetweenThem()
	{
		string html = await RenderAsync(SessionToolbarLayout.Grouped, Actions(), []);

		Assert.Equal(["files", "find", "record", "tools", "close", "more"], Buttons(html));
		Assert.Equal(4, Regex.Count(html, "class=\"mt-tb-divider"));
	}

	[Fact]
	public async Task Flat_DrawsEveryButtonInOneRow_WithoutLines()
	{
		string html = await RenderAsync(SessionToolbarLayout.Flat, Actions(), []);

		Assert.Equal(["files", "find", "record", "tool.docker", "close", "more"], Buttons(html));
		Assert.DoesNotContain("mt-tb-divider", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Compact_DrawsOnlyTheMenu_AndNamesItForTheSession()
	{
		string html = await RenderAsync(SessionToolbarLayout.Compact, Actions(), []);

		Assert.Equal(["more"], Buttons(html));
		Assert.Contains("aria-label=\"Session actions\"", html, StringComparison.Ordinal);
		Assert.DoesNotContain("mt-tb-more--fold", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task NothingHidden_TheOverflowButtonOnlyShowsOnceTheBarFolds()
	{
		string html = await RenderAsync(SessionToolbarLayout.Grouped, Actions(), []);

		Assert.Contains("mt-tb-more mt-tb-more--fold", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AHiddenButton_LeavesTheBar_AndTheOverflowButtonStays()
	{
		string html = await RenderAsync(SessionToolbarLayout.Grouped, Actions(), ["find"]);

		Assert.DoesNotContain("data-button=\"find\"", html, StringComparison.Ordinal);
		Assert.DoesNotContain("mt-tb-more--fold", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Recording_PutsARedDotOnItsIcon_AndATooltipShowsTheShortcut()
	{
		string html = await RenderAsync(SessionToolbarLayout.Grouped, Actions(), []);

		Assert.Contains("class=\"mt-icon-dot\" style=\"--mt-icon-dot:var(--moka-color-error)\"", html, StringComparison.Ordinal);
		Assert.Contains("Disconnect and close (Ctrl+Shift+W)", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
	}

	[Fact]
	public async Task AnOpenTool_LightsTheToolsMenu()
	{
		string html = await RenderAsync(SessionToolbarLayout.Grouped, Actions(), []);

		Match tools = ToolsButton().Match(html);
		Assert.True(tools.Success);
		Assert.Contains("aria-pressed=\"true\"", tools.Value, StringComparison.Ordinal);
	}

	private static List<ToolbarAction> Actions() =>
	[
		Action("files", ToolbarGroup.Panels),
		Action("find", ToolbarGroup.View),
		Action("record", ToolbarGroup.Session) with { Active = true, Toggle = true, Dot = ToolbarDot.Recording },
		Action("tool.docker", ToolbarGroup.Tools) with { Active = true, Toggle = true },
		Action(SessionToolbarButtons.ToolsMenu, ToolbarGroup.Tools) with { Active = true },
		Action("close", ToolbarGroup.Connection) with { Title = "Disconnect and close", Shortcut = "Ctrl+Shift+W" },
	];

	private static ToolbarAction Action(string id, ToolbarGroup group) => new()
	{
		Id = id,
		Title = id,
		Icon = MokaIcons.Action.Search,
		Group = group,
		Run = _ => Task.CompletedTask,
	};

	private static List<string> Buttons(string html) => [.. DataButton().Matches(html).Select(match => match.Groups[1].Value)];

	private static Task<string> RenderAsync(SessionToolbarLayout layout, IReadOnlyList<ToolbarAction> actions, IReadOnlyCollection<string> hidden) =>
		StaticRender.RenderAsync<SessionToolbar>(
			services => services.AddMokaRed().AddScoped<FormInterop>().AddScoped<MenuPlacement>(),
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				[nameof(SessionToolbar.Actions)] = actions,
				[nameof(SessionToolbar.Layout)] = layout,
				[nameof(SessionToolbar.Hidden)] = hidden,
			}).WaitAsync(Patience, Ct);

	[GeneratedRegex("data-button=\"([^\"]+)\"")]
	private static partial Regex DataButton();

	[GeneratedRegex("data-button=\"tools\"[\\s\\S]*?</button>")]
	private static partial Regex ToolsButton();
}
