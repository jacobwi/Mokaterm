using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Settings.Pages.Triggers;
using Mokaterm.UI.Terminal.Triggers;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

/// <summary>
/// Rendering the page proves it builds from its descriptor's services, lists the rules in the order they run and says
/// what each does. Checking a pattern needs the page's script, which a static render never reaches.
/// </summary>
public sealed class TriggerSettingsPageTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Render_ListsTheRulesInOrder_WithWhatEachDoes()
	{
		TerminalTriggerSettings settings = new()
		{
			Rules =
			[
				new TerminalTrigger { Id = Guid.NewGuid(), Pattern = "ERROR|FATAL", Highlight = TriggerColor.Red, IgnoreCase = true },
				new TerminalTrigger { Id = Guid.NewGuid(), Pattern = "build succeeded", Notify = true, Enabled = false },
			],
		};

		string html = await RenderAsync(settings);

		Assert.Contains("ERROR|FATAL", html, StringComparison.Ordinal);
		Assert.Contains("Colors it red, any case", html, StringComparison.Ordinal);
		Assert.Contains("Shows a notice", html, StringComparison.Ordinal);
		Assert.True(html.IndexOf("ERROR|FATAL", StringComparison.Ordinal) < html.IndexOf("build succeeded", StringComparison.Ordinal));
		Assert.Contains("Move build succeeded up", html, StringComparison.Ordinal);
		Assert.Contains("Delete ERROR|FATAL", html, StringComparison.Ordinal);
		Assert.Contains("#ef5350", html, StringComparison.Ordinal);
		Assert.Contains("Add rule", html, StringComparison.Ordinal);
		Assert.Contains("Sample line", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_WithoutRules_SaysWhatTheyAreFor()
	{
		string html = await RenderAsync(new TerminalTriggerSettings());

		Assert.Contains("No triggers yet", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_WhereTheHostHasSystemNotifications_OffersThem()
	{
		string html = await RenderAsync(new TerminalTriggerSettings { SystemNotifications = false }, new NoWindow());

		Assert.Contains("Show a Windows notification in the background", html, StringComparison.Ordinal);
		Assert.Contains("Include the matched line", html, StringComparison.Ordinal);
		Assert.Contains("at most once every 5 seconds", html, StringComparison.Ordinal);
	}

	// A browser asks the user first, so the web hosts offer that, in a browser's words.
	[Fact]
	public async Task Render_WhereTheBrowserShowsThem_OffersToAsk()
	{
		BrowserStandIn browser = new();
		string html = await RenderAsync(new TerminalTriggerSettings(), browser, browser);

		Assert.Contains("Allow notifications in this browser", html, StringComparison.Ordinal);
		Assert.Contains("Show a browser notification in the background", html, StringComparison.Ordinal);
		Assert.Contains("Include the matched line in browser notifications", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Windows notification", html, StringComparison.Ordinal);
	}

	// A host that shows none leaves the section out, since a row there would change nothing.
	[Fact]
	public async Task Render_WithoutSystemNotifications_LeavesThemOut()
	{
		string html = await RenderAsync(new TerminalTriggerSettings());

		Assert.DoesNotContain("Windows notification", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(
		TerminalTriggerSettings settings,
		ISystemNotifications? notifications = null,
		ISystemNotificationConsent? consent = null) =>
		StaticRender.RenderAsync<TriggerSettingsPage>(
			services =>
			{
				services.AddMokaRed();
				services.AddMokatermUiCommon();
				services.AddScoped<TerminalTriggerTester>();
				services.AddSingleton<ISettingsService>(new FixedSettings(settings));
				services.AddSingleton<IUserInteraction>(new DismissingInteraction());
				if (notifications is not null)
				{
					services.AddSingleton(notifications);
				}

				if (consent is not null)
				{
					services.AddSingleton(consent);
				}
			},
			new Dictionary<string, object?>(StringComparer.Ordinal))
			.WaitAsync(TimeSpan.FromSeconds(5), Ct);

	private sealed class NoWindow : ISystemNotifications
	{
		public bool IsSessionWindowInForeground => true;

		public bool Show(SystemNotification notification) => false;
	}

	private sealed class BrowserStandIn : ISystemNotifications, ISystemNotificationConsent
	{
		public bool IsSessionWindowInForeground => true;

		public bool Show(SystemNotification notification) => false;

		public ValueTask<SystemNotificationConsent> GetAsync(CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(SystemNotificationConsent.NotAsked);

		public ValueTask<SystemNotificationConsent> RequestAsync(CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(SystemNotificationConsent.Granted);
	}
}
