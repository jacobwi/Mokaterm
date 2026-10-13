using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Plugins;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Settings.Pages.Plugins;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The Plugins page's Browse tab and what the gallery adds to the Installed tab, rendered for real: each state of the
/// tab, what a card offers against what is installed, the update and withdrawn marks on installed rows, and that the
/// gallery is not asked for anything until the tab opens.
/// </summary>
public sealed partial class PluginGalleryViewTests
{
	private const string Commit = "9f2c4e1a0b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e";

	private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Browse_WhileTheListLoads_SaysWhoIsAskedAndThatItIsReading()
	{
		FakePluginGallery gallery = new() { NeverAnswers = true };

		string html = await StaticRender.RenderUnsettledAsync<PluginGalleryView>(services => AddServices(services, new FakePluginManager(), gallery), Parameters())
			.WaitAsync(Limit, Ct);

		Assert.Contains("Opening this tab asks jacobwi.github.io for the plugin list. Nothing about you or this computer is sent.", html, StringComparison.Ordinal);
		Assert.Contains("Reading the plugin list from jacobwi.github.io", html, StringComparison.Ordinal);
		Assert.Equal(1, gallery.Reads);
	}

	[Fact]
	public async Task Browse_WhenTheListFails_SaysWhyAndOffersToTryAgain()
	{
		FakePluginGallery gallery = new() { Failure = new PluginGalleryException("The plugin list has no signature, so it was not used.") };

		string html = await RenderViewAsync(gallery);

		Assert.Contains("The plugin list could not be read", html, StringComparison.Ordinal);
		Assert.Contains("The plugin list has no signature, so it was not used.", html, StringComparison.Ordinal);
		Assert.Contains("Try again", html, StringComparison.Ordinal);
		Assert.DoesNotContain("plugin-card", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Browse_WithAnEmptyList_SaysTheGalleryIsEmpty()
	{
		string html = await RenderViewAsync(new FakePluginGallery { List = Index() });

		Assert.Contains("The gallery is empty", html, StringComparison.Ordinal);
		Assert.Contains("Refresh", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Browse_DrawsACardPerPlugin_WithWhatEachOneOffers()
	{
		FakePluginGallery gallery = new()
		{
			List = Index(
				Entry("moka.certificates", "Certificates", hasCode: true, verified: true, ["security", "utilities"], Version("1.0.0")),
				Entry("acme.themes", "Night themes", hasCode: false, verified: false, ["themes"], Version("1.1.0"), Version("1.0.0")),
				Entry("acme.proxmox", "Proxmox", hasCode: true, verified: false, ["server-tools"], Version("2.0.0")),
				Entry("acme.waiting", "Waiting", hasCode: true, verified: false, ["utilities"], Version("1.0.0")),
				Entry("acme.future", "Future", hasCode: true, verified: false, ["protocols"], []) with
				{
					Versions = [Version("3.0.0") with { Sdk = "9.9" }],
					Unavailable = "No version of it is built for Mokaterm 0.2 yet.",
				}),
		};
		IReadOnlyList<PluginRecord> installed =
		[
			Installed("acme.themes", "1.0.0"),
			Installed("acme.proxmox", "2.0.0"),
			Installed("acme.waiting", "1.0.0") with { Pending = PluginPendingChange.Install },
		];

		string html = await RenderViewAsync(gallery, installed);

		Assert.Contains(">Certificates<", html, StringComparison.Ordinal);
		Assert.Matches(">\\s*Verified\\s*<", html);
		Assert.Single(VerifiedBadges().Matches(html));
		Assert.Contains(">CODE<", html, StringComparison.Ordinal);
		Assert.Contains(">DATA<", html, StringComparison.Ordinal);
		Assert.Contains(">Security<", html, StringComparison.Ordinal);
		Assert.Contains(">Server tools<", html, StringComparison.Ordinal);
		Assert.Contains(">MIT<", html, StringComparison.Ordinal);
		Assert.Contains(">Desktop<", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Source of Certificates\"", Tooltips.ControlFor(html, "https://github.com/acme/moka.certificates"), StringComparison.Ordinal);

		// Not installed, an update, the same version, a waiting install, and nothing for this Mokaterm.
		Assert.False(IsDisabled(Button(html, "Install Certificates")));
		Assert.Contains("Update to 1.1.0", html, StringComparison.Ordinal);
		Assert.False(IsDisabled(Button(html, "Update Night themes to 1.1.0")));
		Assert.Matches("(?s)data-plugin=\"acme.proxmox\"(?:(?!</article>).)*>\\s*Installed\\s*<", html);
		Assert.Matches("(?s)data-plugin=\"acme.waiting\"(?:(?!</article>).)*Waits for restart", html);
		Assert.Contains("No version of it is built for Mokaterm 0.2 yet.", html, StringComparison.Ordinal);
		Assert.DoesNotContain("aria-label=\"Install Future\"", html, StringComparison.Ordinal);
		Assert.Contains(">3.0.0<", html, StringComparison.Ordinal);

		// Something here fits, so there is no note saying nothing does.
		Assert.DoesNotContain("Nothing here fits this Mokaterm", html, StringComparison.Ordinal);

		// One chip per category the list uses, known ones in a fixed order, after All.
		Assert.Equal(
			["All", "Protocols", "Server tools", "Utilities", "Security", "Themes"],
			ChipTexts().Matches(html).Select(match => match.Groups[1].Value));
	}

	[Fact]
	public async Task Browse_WhileADownloadRuns_DisablesEveryInstall()
	{
		FakePluginGallery gallery = new()
		{
			List = Index(
				Entry("moka.certificates", "Certificates", hasCode: true, verified: true, ["security"], Version("1.0.0")),
				Entry("acme.proxmox", "Proxmox", hasCode: true, verified: false, ["server-tools"], Version("2.0.0"))),
		};

		string html = await RenderViewAsync(gallery, busy: true, downloading: "moka.certificates");

		Assert.True(IsDisabled(Button(html, "Install Certificates")));
		Assert.True(IsDisabled(Button(html, "Install Proxmox")));
	}

	[Fact]
	public async Task Browse_WhenNothingFitsThisMokaterm_SaysSo()
	{
		FakePluginGallery gallery = new()
		{
			List = Index(Entry("acme.future", "Future", hasCode: true, verified: false, ["protocols"], []) with
			{
				Versions = [Version("3.0.0") with { Sdk = "9.9" }],
				Unavailable = "No version of it is built for Mokaterm 0.2 yet.",
			}),
		};

		string html = await RenderViewAsync(gallery);

		Assert.Contains("Nothing here fits this Mokaterm", html, StringComparison.Ordinal);
		Assert.Contains("No version of it is built for Mokaterm 0.2 yet.", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Browse_WithAListThisWindowAlreadyRead_DoesNotAskAgain()
	{
		FakePluginGallery gallery = new() { Current = Index(Entry("acme.proxmox", "Proxmox", hasCode: true, verified: false, ["server-tools"], Version("2.0.0"))) };

		string html = await RenderViewAsync(gallery);

		Assert.Contains(">Proxmox<", html, StringComparison.Ordinal);
		Assert.Equal(0, gallery.Reads);
	}

	[Fact]
	public async Task Page_WithAGallery_HasInstalledAndBrowseTabs_AndAsksTheGalleryNothing()
	{
		FakePluginGallery gallery = new() { List = Index() };

		string html = await RenderPageAsync(new FakePluginManager { Plugins = [Installed("acme.proxmox", "2.0.0")] }, gallery);

		Assert.Contains("role=\"tablist\"", html, StringComparison.Ordinal);
		Assert.Matches("role=\"tab\"[^>]*aria-selected=\"true\"[^>]*>\\s*Installed\\s*<", html);
		Assert.Matches("role=\"tab\"[^>]*aria-selected=\"false\"[^>]*>\\s*Browse\\s*<", html);
		Assert.Contains(">acme.proxmox<", html, StringComparison.Ordinal);
		Assert.DoesNotContain("plugin-gallery", html, StringComparison.Ordinal);
		Assert.Equal(0, gallery.Reads);
	}

	[Fact]
	public async Task Page_WithoutAGallery_OrWhereNothingInstalls_HasNoTabs()
	{
		string withoutGallery = await RenderPageAsync(new FakePluginManager(), gallery: null);
		string webHost = await RenderPageAsync(new FakePluginManager { CanInstall = false }, new FakePluginGallery { List = Index() });

		Assert.DoesNotContain("role=\"tablist\"", withoutGallery, StringComparison.Ordinal);
		Assert.DoesNotContain("role=\"tablist\"", webHost, StringComparison.Ordinal);
		Assert.Contains("No plugins installed", webHost, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Page_OnceTheListWasRead_MarksUpdatesAndWithdrawnVersions()
	{
		PluginGalleryVersion withdrawn = Version("1.0.0") with { Withdrawn = "Its selection colour hid the cursor." };
		FakePluginGallery gallery = new()
		{
			Current = Index(

				// The withdrawn 1.0.0 is listed but not offered.
				Entry("acme.themes", "Night themes", hasCode: false, verified: false, ["themes"], Version("1.1.0"), withdrawn) with
				{
					Offered = [Version("1.1.0")],
				},
				Entry("acme.proxmox", "Proxmox", hasCode: true, verified: false, ["server-tools"], Version("2.0.0")),
				Entry("acme.newer", "Newer", hasCode: true, verified: false, ["utilities"], Version("1.1.0"))),
		};

		string html = await RenderPageAsync(
			new FakePluginManager
			{
				Plugins =
				[
					Installed("acme.themes", "1.0.0"),
					Installed("acme.proxmox", "2.0.0"),
					Installed("acme.newer", "1.0.0") with { Pending = PluginPendingChange.Update },
				],
			},
			gallery);

		Assert.Matches("(?s)data-plugin=\"acme.themes\"(?:(?!data-plugin=).)*Update available: 1.1.0", html);
		Assert.Contains("Version 1.0.0 was withdrawn from the gallery. Its selection colour hid the cursor.", html, StringComparison.Ordinal);
		Assert.DoesNotMatch("(?s)data-plugin=\"acme.proxmox\"(?:(?!data-plugin=).)*Update available", html);

		// An update already waiting for the next start is not offered again.
		Assert.DoesNotMatch("(?s)data-plugin=\"acme.newer\"(?:(?!data-plugin=).)*Update available", html);
		Assert.Equal(0, gallery.Reads);
	}

	[Fact]
	public async Task Page_BeforeTheListWasRead_MarksNothing()
	{
		FakePluginGallery gallery = new() { List = Index(Entry("acme.themes", "Night themes", hasCode: false, verified: false, ["themes"], Version("1.1.0"))) };

		string html = await RenderPageAsync(new FakePluginManager { Plugins = [Installed("acme.themes", "1.0.0")] }, gallery);

		Assert.DoesNotContain("Update available", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task InstallDialog_ForAGalleryPackage_SaysWhereItWasBuiltFrom()
	{
		FakePluginPackage package = new()
		{
			Id = "moka.certificates",
			Name = "Certificates",
			Publisher = "Moka",
			HasCode = true,
			Origin = new PluginPackageOrigin(new Uri("https://github.com/jacobwi/mokaterm-plugins"), Commit, Verified: true),
		};

		string html = await StaticRender.RenderAsync<PluginInstallDialog>(
			services => AddServices(services, new FakePluginManager(), gallery: null),
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				[nameof(PluginInstallDialog.Open)] = true,
				[nameof(PluginInstallDialog.OpenChanged)] = EventCallback.Factory.Create<bool>(new object(), static _ => { }),
				[nameof(PluginInstallDialog.Package)] = package,
			})
			.WaitAsync(Limit, Ct);

		Assert.Contains("From the Mokaterm gallery, built from https://github.com/jacobwi/mokaterm-plugins at 9f2c4e1", html, StringComparison.Ordinal);
		Assert.Contains($"title=\"Commit {Commit}\"", html, StringComparison.Ordinal);
		Assert.Matches(">\\s*Verified\\s*<", html);

		// The trust step is the same one a picked file gets.
		Assert.Contains("I trust this publisher", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task InstallDialog_ForAPickedFile_NamesNoOrigin()
	{
		string html = await StaticRender.RenderAsync<PluginInstallDialog>(
			services => AddServices(services, new FakePluginManager(), gallery: null),
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				[nameof(PluginInstallDialog.Open)] = true,
				[nameof(PluginInstallDialog.OpenChanged)] = EventCallback.Factory.Create<bool>(new object(), static _ => { }),
				[nameof(PluginInstallDialog.Package)] = new FakePluginPackage { Id = "acme.themes", Name = "Night themes" },
			})
			.WaitAsync(Limit, Ct);

		Assert.DoesNotContain("From the Mokaterm gallery", html, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(null, PluginPendingChange.None, nameof(PluginGalleryOfferKind.Install), "1.1.0")]
	[InlineData("1.0.0", PluginPendingChange.Remove, nameof(PluginGalleryOfferKind.Install), "1.1.0")]
	[InlineData("1.0.0", PluginPendingChange.None, nameof(PluginGalleryOfferKind.Update), "1.1.0")]
	[InlineData("1.1.0", PluginPendingChange.None, nameof(PluginGalleryOfferKind.Installed), null)]
	[InlineData("1.2.0", PluginPendingChange.None, nameof(PluginGalleryOfferKind.Installed), null)]
	[InlineData("1.0.0", PluginPendingChange.Update, nameof(PluginGalleryOfferKind.Waiting), null)]
	[InlineData("1.1.0", PluginPendingChange.Install, nameof(PluginGalleryOfferKind.Waiting), null)]
	public void Offer_DependsOnWhatIsInstalled(string? installed, PluginPendingChange pending, string kind, string? version)
	{
		PluginGalleryEntry entry = Entry("acme.themes", "Night themes", hasCode: false, verified: false, ["themes"], Version("1.1.0"));
		PluginRecord? record = installed is null ? null : Installed("acme.themes", installed) with { Pending = pending };

		PluginGalleryOffer offer = PluginGalleryOffer.For(entry, record);

		Assert.Equal(Enum.Parse<PluginGalleryOfferKind>(kind), offer.Kind);
		Assert.Equal(version, offer.Version?.Version);
	}

	[Fact]
	public void Offer_WithNothingOffered_IsUnavailable() =>
		Assert.Equal(
			PluginGalleryOfferKind.Unavailable,
			PluginGalleryOffer.For(Entry("acme.future", "Future", hasCode: true, verified: false, ["protocols"], []), installed: null).Kind);

	private static PluginGalleryIndex Index(params PluginGalleryEntry[] plugins) => new()
	{
		Generated = DateTimeOffset.UnixEpoch,
		Plugins = plugins,
	};

	/// <summary>An entry whose listed versions are all offered, as the gallery would have worked out.</summary>
	private static PluginGalleryEntry Entry(string id, string name, bool hasCode, bool verified, string[] categories, params PluginGalleryVersion[] versions) => new()
	{
		Id = id,
		Name = name,
		Publisher = "Acme",
		Description = $"What {name} does.",
		Categories = categories,
		Source = new Uri($"https://github.com/acme/{id}"),
		License = "MIT",
		Verified = verified,
		HasCode = hasCode,
		Hosts = [PluginHosts.Desktop, PluginHosts.Web],
		Versions = versions,
		Offered = versions,
		Unavailable = versions.Length == 0 ? "The gallery lists no version of it." : null,
	};

	private static PluginGalleryVersion Version(string version) => new()
	{
		Version = version,
		Sdk = "0.2",
		Url = new Uri($"https://downloads.test/{version}.mokaplugin"),
		Sha256 = new string('a', 64),
		Size = 1024,
		Commit = Commit,
		Published = DateTimeOffset.UnixEpoch,
	};

	private static PluginRecord Installed(string id, string version) => new()
	{
		Id = id,
		Name = id,
		Version = version,
		Status = PluginStatus.Enabled,
		IsEnabled = true,
	};

	private static Dictionary<string, object?> Parameters(IReadOnlyList<PluginRecord>? installed = null, bool busy = false, string? downloading = null) =>
		new(StringComparer.Ordinal)
		{
			[nameof(PluginGalleryView.Installed)] = installed ?? [],
			[nameof(PluginGalleryView.Busy)] = busy,
			[nameof(PluginGalleryView.Downloading)] = downloading,
			[nameof(PluginGalleryView.OnInstall)] = EventCallback.Factory.Create<PluginGalleryEntry>(new object(), static _ => { }),
		};

	private static Task<string> RenderViewAsync(FakePluginGallery gallery, IReadOnlyList<PluginRecord>? installed = null, bool busy = false, string? downloading = null) =>
		StaticRender.RenderAsync<PluginGalleryView>(
			services => AddServices(services, new FakePluginManager(), gallery),
			Parameters(installed, busy, downloading))
			.WaitAsync(Limit, Ct);

	private static Task<string> RenderPageAsync(FakePluginManager plugins, FakePluginGallery? gallery) =>
		StaticRender.RenderAsync<PluginsPage>(
			services =>
			{
				AddServices(services, plugins, gallery);
				services.AddSingleton<ILocalFileAccess, NoLocalFiles>();
			},
			new Dictionary<string, object?>(StringComparer.Ordinal))
			.WaitAsync(Limit, Ct);

	private static void AddServices(IServiceCollection services, FakePluginManager plugins, FakePluginGallery? gallery)
	{
		services.AddMokaRed();
		services.AddMokatermUiCommon();
		services.AddSingleton(TimeProvider.System);
		services.AddSingleton<ISettingsService>(new FixedSettings());
		services.AddSingleton<IUserInteraction>(new DismissingInteraction());
		services.AddSingleton<IPluginManager>(plugins);
		if (gallery is not null)
		{
			services.AddSingleton<IPluginGallery>(gallery);
		}
	}

	/// <summary>The opening tag of the button the page names with <paramref name="label"/>.</summary>
	private static string Button(string html, string label)
	{
		Match match = Regex.Match(html, $"<button[^>]*aria-label=\"{Regex.Escape(label)}\"[^>]*>", RegexOptions.None, TimeSpan.FromSeconds(1));
		Assert.True(match.Success, $"No button named {label}.");
		return match.Value;
	}

	private static bool IsDisabled(string openingTag) => DisabledAttribute().IsMatch(openingTag);

	[GeneratedRegex("\\sdisabled(?:[\\s=>/]|$)")]
	private static partial Regex DisabledAttribute();

	[GeneratedRegex("class=\"plugin-verified\"")]
	private static partial Regex VerifiedBadges();

	[GeneratedRegex("<button[^>]*class=\"[^\"]*moka-chip[^\"]*\"[^>]*>(?:(?!</button>).)*?<span class=\"moka-chip__text\"[^>]*>([^<]+)</span>", RegexOptions.Singleline)]
	private static partial Regex ChipTexts();
}
