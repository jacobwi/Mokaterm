using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Tests.Shared;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Common.ToolPages;
using Mokaterm.UI.Tests.Fakes;
using Mokaterm.UI.Tools;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The one frame every tool is drawn in. A dialog and a window carry the same icon, title and context; the actions are
/// the ones that fit where the tool is and what the host can do; a session tool says how its session is doing in the
/// words the rest of the app uses.
/// </summary>
public sealed partial class ToolFrameTests
{
	private static readonly SessionToolDescriptor Docker = new()
	{
		ProtocolId = "ssh",
		Id = "docker",
		Title = "Docker",
		Icon = MokatermIcons.Modules,
		Component = typeof(ToolBody),
		Size = SessionToolSize.Wide,
	};

	private static readonly AppToolDescriptor Subnets = new()
	{
		Id = "subnet-calculator",
		Title = "Subnet calculator",
		Icon = MokatermIcons.Globe,
		Description = "Network, broadcast and host range of a prefix",
		Component = typeof(AppBody),
	};

	[Fact]
	public async Task SessionTool_InADialog_NamesTheToolTheSessionAndItsState()
	{
		string html = await RenderSessionToolAsync(ToolPresentation.Dialog, new FakeToolWindows());

		Assert.Contains("class=\"moka-dialog-title\"", html, StringComparison.Ordinal);
		Assert.Contains(">Docker</span>", html, StringComparison.Ordinal);
		Assert.Contains("build-01.example.com", html, StringComparison.Ordinal);
		Assert.Contains("CONNECTED", html, StringComparison.Ordinal);
		Assert.Contains("moka-status-dot--success", html, StringComparison.Ordinal);
		Assert.Contains("the docker tool", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task SessionTool_InAWindow_DrawsTheSameHeaderItself()
	{
		string html = await RenderSessionToolAsync(ToolPresentation.Window, new FakeToolWindows());

		Assert.DoesNotContain("moka-dialog", html, StringComparison.Ordinal);
		Assert.Contains("class=\"mt-dialog-head", html, StringComparison.Ordinal);
		Assert.Contains(">Docker</span>", html, StringComparison.Ordinal);
		Assert.Contains("build-01.example.com", html, StringComparison.Ordinal);
		Assert.Contains("CONNECTED", html, StringComparison.Ordinal);
		Assert.Contains("the docker tool", html, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(SessionState.Connected, "CONNECTED", "moka-status-dot--success", false)]
	[InlineData(SessionState.Connecting, "CONNECTING", "moka-status-dot--warning", true)]
	[InlineData(SessionState.Disconnected, "DISCONNECTED", "moka-status-dot--surface", false)]
	[InlineData(SessionState.Failed, "FAILED", "moka-status-dot--error", false)]
	public async Task SessionTool_EachState_HasTheTabsWordAndColour(SessionState state, string label, string colour, bool pulses)
	{
		string html = await RenderSessionToolAsync(ToolPresentation.Window, new FakeToolWindows(), session => session.Become(state));
		string dot = StatusDot().Match(html).Value;

		Assert.Contains(label, dot, StringComparison.Ordinal);
		Assert.Contains(colour, dot, StringComparison.Ordinal);
		Assert.Equal(pulses, dot.Contains("moka-status-dot--pulse", StringComparison.Ordinal));
		Assert.Contains("moka-status-dot--glow", dot, StringComparison.Ordinal);
	}

	// A drop that will be retried: the dot says FAILED and its tooltip carries the session's own countdown.
	[Fact]
	public async Task SessionTool_Reconnecting_SaysWhenTheNextAttemptIs()
	{
		const string countdown = "Connection reset. Reconnecting in 5 s (attempt 1 of 3).";
		string html = await RenderSessionToolAsync(
			ToolPresentation.Window,
			new FakeToolWindows(),
			session => session.Become(SessionState.Failed, countdown));

		Assert.Contains($"title=\"{countdown}\"", StatusDot().Match(html).Value, StringComparison.Ordinal);
	}

	[Fact]
	public async Task SessionTool_InAWindow_ThatLostItsSession_SaysWhyInsteadOfShowingTheTool()
	{
		string html = await RenderSessionToolAsync(
			ToolPresentation.Window,
			new FakeToolWindows(),
			session => session.Become(SessionState.Disconnected));

		Assert.DoesNotContain("the docker tool", html, StringComparison.Ordinal);
		Assert.Contains("Docker needs the session", html, StringComparison.Ordinal);

		// Its dialog would not open while the session is down, so there is no way back to offer.
		Assert.Equal(["Keep on top", "Show the main window"], Actions(html));
	}

	// By default the header is the icon and the title; what the tool does shows when the title is pointed at.
	[Theory]
	[InlineData(ToolPresentation.Dialog)]
	[InlineData(ToolPresentation.Window)]
	public async Task AppTool_ByDefault_KeepsItsDescriptionOutOfTheHeader(ToolPresentation presentation)
	{
		string html = await RenderAppToolAsync(presentation, new FakeToolWindows());

		Assert.Contains(
			"<span class=\"mt-dialog-title-text\" title=\"Network, broadcast and host range of a prefix\"",
			TitleScope().Replace(html, ""),
			StringComparison.Ordinal);
		Assert.DoesNotContain("mt-dialog-detail", html, StringComparison.Ordinal);
		Assert.DoesNotContain("mt-tool-description", html, StringComparison.Ordinal);
		Assert.Contains("the subnet tool", html, StringComparison.Ordinal);
		Assert.DoesNotContain("moka-status-dot", html, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(ToolPresentation.Dialog, "class=\"moka-dialog-title\"")]
	[InlineData(ToolPresentation.Window, "class=\"mt-dialog-head")]
	public async Task AppTool_WithDescriptionsOn_PutsItAfterTheTitle(ToolPresentation presentation, string header)
	{
		string html = await RenderAppToolAsync(
			presentation,
			new FakeToolWindows(),
			appearance: new AppearanceSettings { ShowToolDescriptions = true });

		Assert.Contains(header, html, StringComparison.Ordinal);
		Assert.Contains(">Subnet calculator</span>", html, StringComparison.Ordinal);
		Assert.Contains("mt-dialog-detail", html, StringComparison.Ordinal);
		Assert.Contains(">Network, broadcast and host range of a prefix</span>", html, StringComparison.Ordinal);
		Assert.DoesNotContain("mt-dialog-title-text\" title=", TitleScope().Replace(html, ""), StringComparison.Ordinal);
	}

	// A session tool has no description: its header carries the session either way.
	[Fact]
	public async Task SessionTool_WithDescriptionsOn_StillCarriesItsSession()
	{
		string html = await RenderSessionToolAsync(
			ToolPresentation.Window,
			new FakeToolWindows(),
			appearance: new AppearanceSettings { ShowToolDescriptions = true });

		Assert.Contains("mt-tool-session", html, StringComparison.Ordinal);
		Assert.DoesNotContain("mt-tool-description", html, StringComparison.Ordinal);
		Assert.DoesNotContain("mt-dialog-title-text\" title=", TitleScope().Replace(html, ""), StringComparison.Ordinal);
	}

	// The desktop: a dialog offers its own window, and only that.
	[Fact]
	public async Task Dialog_WhereTheHostHasWindows_OffersOnlyItsOwnWindow()
	{
		string session = await RenderSessionToolAsync(ToolPresentation.Dialog, new FakeToolWindows());
		string app = await RenderAppToolAsync(ToolPresentation.Dialog, new FakeToolWindows());

		foreach (string html in new[] { session, app })
		{
			Assert.Equal(["Open in its own window"], Actions(html));
		}
	}

	// The web host and the DevHost: nowhere to pop out to, so the header holds only Moka's close button.
	[Fact]
	public async Task Dialog_WhereTheHostHasNoWindows_OffersNothing()
	{
		string session = await RenderSessionToolAsync(ToolPresentation.Dialog, windows: null);
		string app = await RenderAppToolAsync(ToolPresentation.Dialog, windows: null);

		foreach (string html in new[] { session, app })
		{
			Assert.Empty(Actions(html));
			Assert.DoesNotContain("moka-dialog-header-actions", html, StringComparison.Ordinal);
			Assert.Contains("aria-label=\"Close\"", html, StringComparison.Ordinal);
		}
	}

	[Fact]
	public async Task Window_OffersTheWayBack_KeepOnTop_AndTheMainWindow()
	{
		string session = await RenderSessionToolAsync(ToolPresentation.Window, new FakeToolWindows());
		string app = await RenderAppToolAsync(ToolPresentation.Window, new FakeToolWindows());

		foreach (string html in new[] { session, app })
		{
			Assert.Equal(["Back to the main window", "Keep on top", "Show the main window"], Actions(html));
			Assert.Contains("aria-pressed=\"false\"", html, StringComparison.Ordinal);
			Assert.DoesNotContain("aria-label=\"Close\"", html, StringComparison.Ordinal);
		}
	}

	// Moka's tooltip names each button, with no browser title hint beside it to show a second, plainer one.
	[Theory]
	[InlineData(ToolPresentation.Dialog)]
	[InlineData(ToolPresentation.Window)]
	public async Task HeaderButtons_AreNamedByMokasTooltip_NotTheBrowsers(ToolPresentation presentation)
	{
		string html = await RenderAppToolAsync(presentation, new FakeToolWindows());
		List<string> tooltips = Tooltips.Texts(html);

		Assert.NotEmpty(Actions(html));
		foreach (string action in Actions(html))
		{
			Assert.Contains(action, tooltips);
		}

		Assert.All(HeaderButtonTag().Matches(html), tag => Assert.DoesNotContain(" title=", tag.Value, StringComparison.Ordinal));
	}

	[Fact]
	public async Task Window_KeptOnTop_LightsItsButton()
	{
		FakeToolWindows windows = new();
		string html = await RenderAppToolAsync(ToolPresentation.Window, windows, async harness =>
		{
			await harness.AppTools.OpenWindowAsync(Subnets);
			harness.Windows.SetKeepOnTop(ToolWindowTarget.AppTool("subnet-calculator"), true);
		});

		Assert.Contains("aria-pressed=\"true\"", html, StringComparison.Ordinal);
		Assert.Contains("mt-dialog-head-button--on", html, StringComparison.Ordinal);
	}

	// A window whose session closed has no dialog to go back to; its header still moves, pins and closes the window.
	[Fact]
	public async Task Window_WithoutAWayBack_KeepsTheOtherActions()
	{
		await using ToolHarness harness = await ToolHarness.CreateAsync(new FakeToolWindows());
		string html = await StaticRender.RenderAsync<ToolFrame>(
			services => Register(services, harness),
			new Dictionary<string, object?>
			{
				[nameof(ToolFrame.Presentation)] = ToolPresentation.Window,
				[nameof(ToolFrame.Target)] = ToolWindowTarget.SessionTool(Guid.NewGuid(), "docker"),
				[nameof(ToolFrame.Icon)] = MokatermIcons.Tools,
				[nameof(ToolFrame.Title)] = "Mokaterm",
			});

		Assert.Equal(["Keep on top", "Show the main window"], Actions(html));
	}

	private static List<string> Actions(string html) =>
		[.. HeaderButton().Matches(html).Select(match => match.Groups["title"].Value)];

	private static async Task<string> RenderSessionToolAsync(
		ToolPresentation presentation,
		FakeToolWindows? windows,
		Action<FakeSession>? arrange = null,
		AppearanceSettings? appearance = null)
	{
		await using ToolHarness harness = await ToolHarness.CreateAsync(windows, appearance, sessionTools: [Docker]);
		FakeSession session = harness.Sessions.Add();
		arrange?.Invoke(session);
		return await StaticRender.RenderAsync<SessionToolFrame>(
			services => Register(services, harness),
			new Dictionary<string, object?>
			{
				[nameof(SessionToolFrame.Presentation)] = presentation,
				[nameof(SessionToolFrame.Session)] = session,
				[nameof(SessionToolFrame.Tool)] = Docker,
			});
	}

	private static async Task<string> RenderAppToolAsync(
		ToolPresentation presentation,
		FakeToolWindows? windows,
		Func<ToolHarness, Task>? arrange = null,
		AppearanceSettings? appearance = null)
	{
		await using ToolHarness harness = await ToolHarness.CreateAsync(windows, appearance, appTools: [Subnets]);
		if (arrange is not null)
		{
			await arrange(harness);
		}

		return await StaticRender.RenderAsync<AppToolFrame>(
			services => Register(services, harness),
			new Dictionary<string, object?>
			{
				[nameof(AppToolFrame.Presentation)] = presentation,
				[nameof(AppToolFrame.Tool)] = Subnets,
			});
	}

	private static void Register(IServiceCollection services, ToolHarness harness)
	{
		services.AddMokaRed();
		services.AddSingleton<IUiContributions>(harness.Contributions);
		services.AddSingleton<IUserInteraction>(harness.Prompts);
		services.AddSingleton<ISettingsService>(harness.Settings);
		services.AddSingleton(harness.Windows);
		services.AddSingleton(harness.SessionTools);
		services.AddSingleton(harness.AppTools);
	}

	[GeneratedRegex("<button[^>]*class=\"mt-dialog-head-button[^\"]*\"[^>]*aria-label=\"(?<title>[^\"]*)\"")]
	private static partial Regex HeaderButton();

	[GeneratedRegex("<button[^>]*class=\"mt-dialog-head-button[^>]*>")]
	private static partial Regex HeaderButtonTag();

	// Blazor's scoped CSS attribute, which sits between an element's name and its attributes.
	[GeneratedRegex(" b-[a-z0-9]{10}")]
	private static partial Regex TitleScope();

	[GeneratedRegex("<span[^>]*class=\"moka-status-dot[^\"]*\"[^>]*>.*?</span>\\s*</span>", RegexOptions.Singleline)]
	private static partial Regex StatusDot();

	/// <summary>Stands in for a session tool, so a test can see the frame put it in its body.</summary>
	private sealed class ToolBody : SessionViewBase
	{
		protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, "the docker tool");
	}

	/// <summary>Stands in for an app tool.</summary>
	private sealed class AppBody : AppToolBase
	{
		protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, "the subnet tool");
	}
}
