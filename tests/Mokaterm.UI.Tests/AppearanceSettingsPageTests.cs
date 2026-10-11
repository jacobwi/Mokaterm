using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Common.Icons;
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

		Assert.Contains("Open tools in their own window", html, StringComparison.Ordinal);
	}

	// A browser keeps tools in dialogs, so the switch would do nothing there.
	[Fact]
	public async Task Render_Web_DoesNotOfferToolWindows()
	{
		string html = await RenderAsync(new AppearanceSettings(), HostKind.Web);

		Assert.DoesNotContain("Open tools in their own window", html, StringComparison.Ordinal);
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
