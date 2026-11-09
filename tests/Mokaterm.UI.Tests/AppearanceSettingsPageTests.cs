using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Core.Theming;
using Moka.Red.Extensions;
using Moka.Red.ThemeGen.Appearance;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Common.Presentation;
using Mokaterm.UI.Settings.Pages.Appearance;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The page used to tell the user "Only complete #rrggbb values are saved" while the stored accent happily held #abc,
/// and its slider offered 0.85 to 1.3 of a range the file kept as 0.75 to 1.5.
/// </summary>
public sealed class AppearanceSettingsPageTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Render_SaysWhichColorFormsItTakes()
	{
		string html = await RenderAsync(new AppearanceSettings());

		Assert.Contains("Use #rgb or #rrggbb.", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Only complete", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ModeIsARowTheSearchCanFind_WithTheStoredModePicked()
	{
		string html = await RenderAsync(new AppearanceSettings { Mode = ThemeMode.Light });

		Assert.Contains("data-setting=\"mode\"", html, StringComparison.Ordinal);
		Assert.Matches("<input[^>]*value=\"Light\"[^>]*checked", html);
		Assert.DoesNotMatch("<input[^>]*value=\"Dark\"[^>]*checked", html);
	}

	// The app has only the design ThemeFactory builds, so the panel must not offer the others.
	[Fact]
	public async Task Render_OffersNoPalette()
	{
		string html = await RenderAsync(new AppearanceSettings());

		Assert.DoesNotContain("value=\"gate\"", html, StringComparison.Ordinal);
		Assert.DoesNotContain("value=\"matrix\"", html, StringComparison.Ordinal);
	}

	// The app stores light or dark, so there is no mode to save following the device as.
	[Fact]
	public async Task Render_OffersNoCardForFollowingTheDevice()
	{
		string html = await RenderAsync(new AppearanceSettings());

		Assert.DoesNotContain("value=\"System\"", html, StringComparison.Ordinal);
	}

	// The page has a confirmed reset of its own, and the panel's would appear in light mode or with any accent.
	[Fact]
	public async Task Render_ThePanelsAddNoResetOfTheirOwn()
	{
		string html = await RenderAsync(new AppearanceSettings { Mode = ThemeMode.Light, AccentColor = "#42a5f5", StatusBarStyle = StatusBarStyle.Chips });

		Assert.DoesNotContain("Back to defaults", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_EveryStatusBarStyleIsACard_WithTheStoredOnePicked()
	{
		string html = await RenderAsync(new AppearanceSettings { StatusBarStyle = StatusBarStyle.Chips });

		Assert.Contains("data-setting=\"status-bar-style\"", html, StringComparison.Ordinal);
		foreach (StatusBarStyle style in Enum.GetValues<StatusBarStyle>())
		{
			Assert.Matches($"<input[^>]*value=\"{style}\"", html);
		}

		Assert.Matches("<input[^>]*value=\"Chips\"[^>]*checked", html);
		Assert.DoesNotMatch("<input[^>]*value=\"Standard\"[^>]*checked", html);
	}

	[Fact]
	public async Task Render_WithTheStatusBarOff_TheStyleCardsAreInert()
	{
		string on = await RenderAsync(new AppearanceSettings());
		string off = await RenderAsync(new AppearanceSettings { ShowStatusBar = false });

		Assert.DoesNotContain("moka-appearance-panel--disabled", on, StringComparison.Ordinal);
		Assert.Contains("moka-appearance-panel--disabled", off, StringComparison.Ordinal);
	}

	[Fact]
	public void StatusBarLooks_RoundTripEveryStyle_AndEveryLookIsOneWeCanStore()
	{
		foreach (StatusBarStyle style in Enum.GetValues<StatusBarStyle>())
		{
			Assert.Equal(style, StatusBarLooks.From(StatusBarLooks.Of(style)));
		}

		foreach (MokaStatusBarLook look in Enum.GetValues<MokaStatusBarLook>())
		{
			Assert.Equal(look, StatusBarLooks.Of(StatusBarLooks.From(look)));
		}
	}

	[Theory]
	[InlineData(StatusBarStyle.Terminal)]
	[InlineData(StatusBarStyle.Standard)]
	public void Choice_APickedStatusBar_IsStored_AndNothingElseMoves(StatusBarStyle style)
	{
		AppearanceSettings settings = new() { Mode = ThemeMode.Light, AccentColor = "#123456", Density = UiDensity.Compact };

		AppearanceSettings next = AppearanceChoice.Apply(settings, AppearanceChoice.ValueFor(settings) with { StatusBar = StatusBarLooks.Of(style) });

		Assert.Equal(settings with { StatusBarStyle = style }, next);
	}

	[Theory]
	[InlineData("blue", "#42a5f5")]
	[InlineData("red", AppearanceSettings.DefaultAccentColor)]
	[InlineData(null, AppearanceSettings.DefaultAccentColor)]
	public void Choice_APickedAccent_StoresItsColor(string? id, string stored)
	{
		AppearanceSettings settings = new() { AccentColor = "#123456" };

		Assert.Equal(stored, AppearanceChoice.Apply(settings, AppearanceChoice.ValueFor(settings) with { Accent = id }).AccentColor);
	}

	[Fact]
	public void Choice_AColorOfTheUsersOwn_IsKeptAndOffered()
	{
		AppearanceSettings settings = new() { AccentColor = "#123456" };

		MokaAppearance value = AppearanceChoice.ValueFor(settings);

		Assert.Contains(AppearanceChoice.AccentsFor(settings), accent => accent.Id == value.Accent && accent.Name == "#123456");
		Assert.Equal("#123456", AppearanceChoice.Apply(settings, value).AccentColor);
	}

	[Fact]
	public void Choice_APickWithNoStoredMode_ChangesNothing()
	{
		Assert.Equal(ThemeMode.Light, AppearanceChoice.ModeFrom(MokaColorMode.System, ThemeMode.Light));
		Assert.Equal(ThemeMode.Dark, AppearanceChoice.ModeFrom(MokaColorMode.System, ThemeMode.Dark));
		Assert.Equal(ThemeMode.Light, AppearanceChoice.ModeFrom(MokaColorMode.Light, ThemeMode.Dark));
		Assert.Equal(ThemeMode.Dark, AppearanceChoice.ModeFrom(MokaColorMode.Dark, ThemeMode.Light));
	}

	[Theory]
	[InlineData("#ef5350")]
	[InlineData("#EF5350")]
	[InlineData("not a color")]
	public void Choice_TheDefaultAccent_IsTheRedSwatch(string stored)
	{
		AppearanceSettings settings = new() { AccentColor = stored };

		Assert.DoesNotContain(AppearanceChoice.AccentsFor(settings), accent => accent.Id == "custom");
		Assert.Equal("red", AppearanceChoice.ValueFor(settings).Accent);
	}

	[Theory]
	[InlineData("#42a5f5", "blue")]
	[InlineData("#42A5F5", "blue")]
	[InlineData("#abc", "custom")]
	public void Choice_AnyOtherAccent_IsPickedInTheList(string stored, string id)
	{
		AppearanceSettings settings = new() { AccentColor = stored };

		Assert.Equal(id, AppearanceChoice.ValueFor(settings).Accent);
		Assert.Contains(AppearanceChoice.AccentsFor(settings), accent => accent.Id == id);
	}

	[Fact]
	public async Task Render_OffersTheWholeFontScaleRangeTheFileKeeps()
	{
		string html = await RenderAsync(new AppearanceSettings());

		Assert.Contains($"min=\"{AppearanceSettings.MinFontScale}\"", html, StringComparison.Ordinal);
		Assert.Contains($"max=\"{AppearanceSettings.MaxFontScale}\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_AnAccentStoredShort_IsShownInTheFieldWithoutAnError()
	{
		string html = await RenderAsync(new AppearanceSettings { AccentColor = "#abc" });

		Assert.Contains("#abc", html, StringComparison.Ordinal);
		Assert.DoesNotContain("moka-field-error", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ListsEveryToolbarButton_AndEveryToolOnce_WithItsSwitch()
	{
		string html = await RenderAsync(new AppearanceSettings { HiddenToolbarButtons = ["find"] });

		foreach (SessionToolbarButton button in SessionToolbarButtons.BuiltIn)
		{
			Assert.Contains($"aria-label=\"Show {button.Title} on the toolbar\"", html, StringComparison.Ordinal);
		}

		Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "aria-label=\"Show Docker on the toolbar\""));
		Assert.Contains("Toolbar layout", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_Desktop_OffersToolWindows()
	{
		string html = await RenderAsync(new AppearanceSettings(), HostKind.Desktop);

		Assert.Contains("Tool windows", html, StringComparison.Ordinal);
		Assert.Contains("Open tools in their own window", html, StringComparison.Ordinal);
		Assert.Contains("Reopen tool windows at start", html, StringComparison.Ordinal);
		Assert.Contains("Remember tool window places", html, StringComparison.Ordinal);
		Assert.Contains("Show tool descriptions", html, StringComparison.Ordinal);
	}

	// A browser keeps tools in dialogs, so the window switches would do nothing there; a dialog's header still has a title.
	[Fact]
	public async Task Render_Web_OffersOnlyWhatADialogUses()
	{
		string html = await RenderAsync(new AppearanceSettings(), HostKind.Web);

		Assert.DoesNotContain("Open tools in their own window", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Reopen tool windows at start", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Remember tool window places", html, StringComparison.Ordinal);
		Assert.Contains("Show tool descriptions", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_OffersTheHostFigures_AndHowOftenTheyAreRead()
	{
		string html = await RenderAsync(new AppearanceSettings());

		Assert.Contains("Live host figures", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Show live host figures\"", html, StringComparison.Ordinal);
		Assert.Contains("Host figures interval", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Host figures interval in seconds\"", html, StringComparison.Ordinal);
	}

	// On by default, every five seconds, and a hand-edited interval is pulled back into the range the page offers.
	[Fact]
	public void HostFigures_AreOnEveryFiveSeconds_AndTheIntervalStaysInItsRange()
	{
		AppearanceSettings defaults = new();

		Assert.True(defaults.ShowHostFigures);
		Assert.Equal(5, defaults.HostFiguresSeconds);
		Assert.Equal(AppearanceSettings.MinHostFiguresSeconds, (defaults with { HostFiguresSeconds = 0 }).Clamped().HostFiguresSeconds);
		Assert.Equal(AppearanceSettings.MaxHostFiguresSeconds, (defaults with { HostFiguresSeconds = 3600 }).Clamped().HostFiguresSeconds);
		Assert.Equal(17, (defaults with { HostFiguresSeconds = 17 }).Clamped().HostFiguresSeconds);

		AppearanceSettings older = System.Text.Json.JsonSerializer.Deserialize<AppearanceSettings>(
			"{\"showStatusBar\":true}",
			System.Text.Json.JsonSerializerOptions.Web)!;
		Assert.True(older.ShowHostFigures);
		Assert.Equal(AppearanceSettings.DefaultHostFiguresSeconds, older.HostFiguresSeconds);
	}

	// The windows open, come back and keep their places; the header holds no description until asked.
	[Fact]
	public void ToolWindowDefaults_AreOn_ExceptDescriptions()
	{
		AppearanceSettings defaults = new();

		Assert.True(defaults.ToolsInOwnWindow);
		Assert.True(defaults.ReopenToolWindows);
		Assert.True(defaults.RememberToolWindowPlaces);
		Assert.False(defaults.ShowToolDescriptions);
		Assert.Equal(defaults, defaults.Clamped());
	}

	// A settings file from before these switches existed reads as their defaults, not as everything off.
	[Fact]
	public void AnOlderFile_WithoutTheToolWindowSwitches_ReadsAsTheirDefaults()
	{
		AppearanceSettings read = System.Text.Json.JsonSerializer.Deserialize<AppearanceSettings>(
			"{\"toolsInOwnWindow\":false}",
			System.Text.Json.JsonSerializerOptions.Web)!;

		Assert.False(read.ToolsInOwnWindow);
		Assert.True(read.ReopenToolWindows);
		Assert.True(read.RememberToolWindowPlaces);
		Assert.False(read.ShowToolDescriptions);
	}

	private static Task<string> RenderAsync(AppearanceSettings settings, HostKind host = HostKind.Web) =>
		StaticRender.RenderAsync<AppearanceSettingsPage>(
			services =>
			{
				services.AddMokaRed();
				services.AddMokatermUiCommon();
				services.AddSingleton<ISettingsService>(new FixedSettings(settings));
				services.AddSingleton<IUserInteraction, SilentInteraction>();
				services.AddSingleton<IAppEnvironment>(new Host(host));
				services.AddSessionTool(Tool("ssh"));
				services.AddSessionTool(Tool("demo"));
			},
			new Dictionary<string, object?>(StringComparer.Ordinal))
			.WaitAsync(TimeSpan.FromSeconds(5), Ct);

	private static SessionToolDescriptor Tool(string protocol) => new()
	{
		ProtocolId = protocol,
		Id = "docker",
		Title = "Docker",
		Icon = MokatermIcons.Modules,
		Component = typeof(NoView),
	};

	/// <summary>A tool's component; the page only lists its name.</summary>
	private sealed class NoView : SessionViewBase;

	private sealed class Host(HostKind kind) : IAppEnvironment
	{
		public HostKind Kind => kind;

		public string PlatformName => kind.ToString();

		public string AppVersion => "0.0.0";

		public string DataDirectory => "";

		public string TempDirectory => "";
	}

	/// <summary>Only there because SettingsPage confirms its reset; nothing here asks anything.</summary>
	private sealed class SilentInteraction : IUserInteraction
	{
		public Task<HostTrustDecision> ConfirmHostIdentityAsync(HostTrustPrompt prompt, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public Task<CredentialPromptResult?> PromptCredentialsAsync(CredentialPrompt prompt, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public Task<SecretPromptResult?> PromptSecretAsync(SecretPrompt prompt, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public Task<IReadOnlyList<string>?> PromptKeyboardInteractiveAsync(KeyboardInteractivePrompt prompt, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public Task<ConfirmResult> ConfirmAsync(ConfirmPrompt prompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<OverwriteDecision> ConfirmOverwriteAsync(OverwritePrompt prompt, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public void Notify(NoticeSeverity severity, string message, string? title = null)
		{
		}
	}
}
