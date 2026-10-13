using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Moka.Red.Core.Enums;
using Moka.Red.Extensions;
using Moka.Red.Feedback.Tooltip;
using Moka.Red.Icons;
using Mokaterm.UI.Common.Components;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The one icon-only button and the one tooltip every view uses: the label is both the tooltip and the accessible name,
/// a shortcut goes after it in the tooltip only, and nothing falls back on the browser's own title tooltip.
/// </summary>
public sealed partial class IconButtonTests
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

	[Fact]
	public async Task TheLabel_IsTheTooltip_AndTheButtonsName()
	{
		string html = await RenderButtonAsync(new() { [nameof(IconButton.Label)] = "Refresh" });

		Assert.Equal(["Refresh"], Tooltips.Texts(html));
		string button = Button(Tooltips.ControlFor(html, "Refresh"));
		Assert.Contains("aria-label=\"Refresh\"", button, StringComparison.Ordinal);
		Assert.Contains("moka-btn--icon-only", button, StringComparison.Ordinal);
		Assert.DoesNotContain("title=", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AShortcut_FollowsTheLabelInTheTooltip_ButNotInTheName()
	{
		string html = await RenderButtonAsync(new()
		{
			[nameof(IconButton.Label)] = "Find files",
			[nameof(IconButton.Shortcut)] = "Ctrl+Shift+F",
		});

		Assert.Equal(["Find files (Ctrl+Shift+F)"], Tooltips.Texts(html));
		Assert.Contains("aria-label=\"Find files\"", Button(html), StringComparison.Ordinal);
	}

	[Fact]
	public async Task AnAriaLabel_NamesTheButton_WhileTheTooltipKeepsTheLabel()
	{
		string html = await RenderButtonAsync(new()
		{
			[nameof(IconButton.Label)] = "Delete",
			[nameof(IconButton.AriaLabel)] = "Delete web01",
		});

		Assert.Equal(["Delete"], Tooltips.Texts(html));
		Assert.Contains("aria-label=\"Delete web01\"", Button(html), StringComparison.Ordinal);
	}

	[Fact]
	public async Task ADisabledButton_KeepsItsTooltip_BesideItRatherThanInsideIt()
	{
		// A disabled Moka button takes no pointer events, so the hover lands on the trigger around it, which is what shows
		// the popup. The popup must therefore sit beside the button inside the trigger, never inside the button.
		string html = await RenderButtonAsync(new()
		{
			[nameof(IconButton.Label)] = "A container still uses it",
			[nameof(IconButton.Disabled)] = true,
		});

		string control = Tooltips.ControlFor(html, "A container still uses it");
		Assert.Matches(DisabledAttribute(), Button(control));
		Assert.EndsWith("</button>", control.TrimEnd(), StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(null, "bottom")]
	[InlineData(MokaTooltipPosition.BottomEnd, "bottom-end")]
	[InlineData(MokaTooltipPosition.Top, "top")]
	public async Task ThePosition_DefaultsToBelow(MokaTooltipPosition? position, string expected)
	{
		Dictionary<string, object?> parameters = new() { [nameof(IconButton.Label)] = "Close" };
		if (position is { } chosen)
		{
			parameters[nameof(IconButton.Position)] = chosen;
		}

		string html = await RenderButtonAsync(parameters);

		Assert.Equal(expected, Tooltips.PositionOf(html, "Close"));
	}

	[Theory]
	[InlineData(null, null)]
	[InlineData(true, "true")]
	[InlineData(false, "false")]
	public async Task Pressed_MakesItAToggle_OnlyWhenSet(bool? pressed, string? expected)
	{
		string html = await RenderButtonAsync(new()
		{
			[nameof(IconButton.Label)] = "Show hidden files",
			[nameof(IconButton.Pressed)] = pressed,
		});

		string button = Button(html);
		if (expected is null)
		{
			Assert.DoesNotContain("aria-pressed", button, StringComparison.Ordinal);
		}
		else
		{
			Assert.Contains($"aria-pressed=\"{expected}\"", button, StringComparison.Ordinal);
		}
	}

	[Fact]
	public async Task Active_LightsTheIconWhateverItsColor_AndOtherAttributesReachTheButton()
	{
		string html = await RenderButtonAsync(new()
		{
			[nameof(IconButton.Label)] = "Bookmarks",
			[nameof(IconButton.Active)] = true,
			[nameof(IconButton.Color)] = MokaColor.Error,
			["aria-haspopup"] = "menu",
		});

		string button = Button(html);
		Assert.Contains("moka-btn--primary", button, StringComparison.Ordinal);
		Assert.Contains("aria-haspopup=\"menu\"", button, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AHintWithoutText_ShowsNothing_ButKeepsItsContent()
	{
		string html = await RenderHintAsync(null, null);

		Assert.Empty(Tooltips.Texts(html));
		Assert.DoesNotContain("role=\"tooltip\"", html, StringComparison.Ordinal);
		Assert.DoesNotContain("data-mt-no-drag", html, StringComparison.Ordinal);
		Assert.Contains(">inside<", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AHint_SaysItsTextAndShortcut()
	{
		string html = await RenderHintAsync("Save", "Ctrl+S");

		Assert.Equal(["Save (Ctrl+S)"], Tooltips.Texts(html));
		Assert.Contains(">inside<", Tooltips.ControlFor(html, "Save (Ctrl+S)"), StringComparison.Ordinal);
	}

	// A sentence wraps rather than running past the window's edge, and a hint in a desktop window's title bar still sees
	// the pointer: window-chrome.js leaves anything marked data-mt-no-drag to the page.
	[Fact]
	public async Task AHint_WrapsItsText_AndKeepsThePointerInThePage()
	{
		const string text = "The server cannot compute checksums, so this copy was not checked";
		string html = await RenderHintAsync(text, null);

		string popup = Tooltips.PopupFor(html, text);
		Assert.Contains("moka-tooltip-popup--wrap", popup, StringComparison.Ordinal);
		Assert.Contains("--moka-tooltip-max-width: 20rem", popup, StringComparison.Ordinal);
		Assert.Contains("data-mt-no-drag", Tooltips.TriggerFor(html, text), StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("Refresh", null, "Refresh")]
	[InlineData("Refresh", "", "Refresh")]
	[InlineData("Refresh", "F5", "Refresh (F5)")]
	[InlineData(null, "F5", null)]
	[InlineData("", "F5", null)]
	public void Format_PutsTheShortcutInBrackets_AndNothingWithoutText(string? text, string? shortcut, string? expected) =>
		Assert.Equal(expected, Hint.Format(text, shortcut));

	private static string Button(string html)
	{
		Match match = ButtonTag().Match(html);
		Assert.True(match.Success, "No button.");
		return match.Value;
	}

	private static Task<string> RenderButtonAsync(Dictionary<string, object?> parameters)
	{
		parameters[nameof(IconButton.Icon)] = MokaIcons.Action.Refresh;
		return StaticRender.RenderAsync<IconButton>(services => services.AddMokaRed(), parameters)
			.WaitAsync(Wait, TestContext.Current.CancellationToken);
	}

	private static Task<string> RenderHintAsync(string? text, string? shortcut) =>
		StaticRender.RenderAsync<Hint>(
			services => services.AddMokaRed(),
			new Dictionary<string, object?>
			{
				[nameof(Hint.Text)] = text,
				[nameof(Hint.Shortcut)] = shortcut,
				[nameof(Hint.ChildContent)] = (RenderFragment)(builder => builder.AddMarkupContent(0, "<b>inside</b>")),
			})
			.WaitAsync(Wait, TestContext.Current.CancellationToken);

	[GeneratedRegex("<button[^>]*>")]
	private static partial Regex ButtonTag();

	[GeneratedRegex("\\sdisabled(?:[\\s=>/]|$)")]
	private static partial Regex DisabledAttribute();
}
