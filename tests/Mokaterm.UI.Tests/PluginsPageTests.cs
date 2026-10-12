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
/// The Plugins page and its install dialog, rendered for real. Every change waits for the next start, so most of what
/// these check is that the page says so: what each plugin is, how this run treated it, what is pending, and that the web
/// host only lists.
/// </summary>
public sealed partial class PluginsPageTests
{
	private const string Sha = "3f79bb7b435b05321651daefd374cdc681dc06faa65e374e38337b88ca046dea";

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Render_ListsEveryPlugin_WithItsKindStatusAndProblem()
	{
		FakePluginManager plugins = new()
		{
			Plugins =
			[
				Plugin("moka.proxmox", "Proxmox", PluginStatus.Enabled, hasCode: true, enabled: true) with
				{
					Publisher = "Moka",
					Description = "Guests, storage and backups on a Proxmox host.",
				},
				Plugin("moka.themes", "Night themes", PluginStatus.Disabled, hasCode: false, enabled: false) with { ThemeCount = 3 },
				Plugin("acme.broken", "Broken", PluginStatus.Failed, hasCode: true, enabled: true) with
				{
					Problem = "The assembly could not be loaded.",
				},
				Plugin("acme.tampered", "Tampered", PluginStatus.Changed, hasCode: true, enabled: true),
				Plugin("acme.old", "Old", PluginStatus.Incompatible, hasCode: true, enabled: false) with
				{
					Hosts = [PluginHosts.Desktop],
					Problem = "Built for Mokaterm 0.1.",
				},
				Plugin("acme.waiting", "Waiting", PluginStatus.Skipped, hasCode: true, enabled: true),
			],
		};

		string html = await RenderPageAsync(plugins);

		foreach (string name in (string[])["Proxmox", "Night themes", "Broken", "Tampered", "Old", "Waiting"])
		{
			Assert.Contains($">{name}<", html, StringComparison.Ordinal);
		}

		foreach (string status in (string[])["On", "Off", "Failed", "Files changed", "Not compatible", "Skipped"])
		{
			Assert.Matches($"moka-status-dot-label\"[^>]*>{Regex.Escape(status)}<", html);
		}

		Assert.Contains(">CODE<", html, StringComparison.Ordinal);
		Assert.Contains(">DATA<", html, StringComparison.Ordinal);
		Assert.Contains(">Moka<", html, StringComparison.Ordinal);
		Assert.Contains("Guests, storage and backups on a Proxmox host.", html, StringComparison.Ordinal);
		Assert.Contains(">3 themes<", html, StringComparison.Ordinal);
		Assert.Contains(">Desktop<", html, StringComparison.Ordinal);
		Assert.Contains(">Web<", html, StringComparison.Ordinal);
		Assert.Contains("The assembly could not be loaded.", html, StringComparison.Ordinal);
		Assert.Contains("Built for Mokaterm 0.1.", html, StringComparison.Ordinal);
		Assert.Contains("Install from file", html, StringComparison.Ordinal);
		Assert.Contains(Path.Combine("C:", "data", "plugins"), html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_OffersTheActionEachStateNeeds()
	{
		FakePluginManager plugins = new()
		{
			Plugins =
			[
				Plugin("moka.on", "Running", PluginStatus.Enabled, hasCode: true, enabled: true),
				Plugin("moka.off", "Resting", PluginStatus.Disabled, hasCode: true, enabled: false),
				Plugin("acme.tampered", "Tampered", PluginStatus.Changed, hasCode: true, enabled: true),
				Plugin("acme.old", "Old", PluginStatus.Incompatible, hasCode: true, enabled: false),
			],
		};

		string html = await RenderPageAsync(plugins);

		Assert.False(IsDisabled(Button(html, "Turn off Running")));
		Assert.False(IsDisabled(Button(html, "Turn on Resting")));
		Assert.False(IsDisabled(Button(html, "Remove Resting")));

		// Changed files are trusted again on purpose, never by the plain switch.
		Assert.Contains("Trust the new files", html, StringComparison.Ordinal);
		Assert.False(IsDisabled(Button(html, "Turn off Tampered")));

		// A plugin made for another version or host has nothing to turn on, but it can still be removed.
		Assert.True(IsDisabled(Button(html, "Turn on Old")));
		Assert.False(IsDisabled(Button(html, "Remove Old")));
	}

	[Fact]
	public async Task Render_ShowsWhatTheNextStartChanges()
	{
		FakePluginManager plugins = new()
		{
			RestartRequired = true,
			Plugins =
			[
				Plugin("moka.later", "Later", PluginStatus.Disabled, hasCode: false, enabled: true) with { Pending = PluginPendingChange.Enable },
				Plugin("moka.leaving", "Leaving", PluginStatus.Enabled, hasCode: true, enabled: true) with { Pending = PluginPendingChange.Remove },
				Plugin("moka.newer", "Newer", PluginStatus.Enabled, hasCode: true, enabled: true) with { Pending = PluginPendingChange.Update },
			],
		};

		string html = await RenderPageAsync(plugins);

		Assert.Contains("Restart Mokaterm to apply", html, StringComparison.Ordinal);
		Assert.Contains("Turns on at the next start.", html, StringComparison.Ordinal);
		Assert.Contains("Removed at the next start.", html, StringComparison.Ordinal);
		Assert.Contains("The new version loads at the next start.", html, StringComparison.Ordinal);

		// A plugin on its way out has nothing left to change.
		Assert.DoesNotContain("aria-label=\"Turn off Leaving\"", html, StringComparison.Ordinal);
		Assert.DoesNotContain("aria-label=\"Remove Leaving\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_WithNothingWaiting_AsksForNoRestart()
	{
		string html = await RenderPageAsync(new FakePluginManager
		{
			Plugins = [Plugin("moka.on", "Running", PluginStatus.Enabled, hasCode: true, enabled: true)],
		});

		Assert.DoesNotContain("Restart Mokaterm to apply", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Plugins are off for this start", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_AfterASafeStart_SaysNoPluginWasLoaded()
	{
		string html = await RenderPageAsync(new FakePluginManager
		{
			IsSafeStart = true,
			Plugins = [Plugin("moka.on", "Running", PluginStatus.Skipped, hasCode: true, enabled: true)],
		});

		Assert.Contains("Plugins are off for this start", html, StringComparison.Ordinal);
		Assert.Contains("MOKATERM_NO_PLUGINS", html, StringComparison.Ordinal);
		Assert.Contains(">Skipped<", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_OnTheWebHost_OnlyListsAndNamesTheOperatorsFolder()
	{
		string folder = Path.Combine("srv", "mokaterm", "plugins");
		string html = await RenderPageAsync(new FakePluginManager
		{
			CanInstall = false,
			Directory = folder,
			Plugins = [Plugin("moka.proxmox", "Proxmox", PluginStatus.Enabled, hasCode: true, enabled: true)],
		});

		Assert.Contains(">Proxmox<", html, StringComparison.Ordinal);
		Assert.Contains(folder, html, StringComparison.Ordinal);
		Assert.Contains("come only from this folder, which the operator manages", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Install from file", html, StringComparison.Ordinal);
		Assert.DoesNotContain("aria-label=\"Turn off Proxmox\"", html, StringComparison.Ordinal);
		Assert.DoesNotContain("aria-label=\"Remove Proxmox\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_WithNothingInstalled_ShowsTheEmptyState()
	{
		string desktop = await RenderPageAsync(new FakePluginManager());
		string web = await RenderPageAsync(new FakePluginManager { CanInstall = false });

		Assert.Contains("No plugins installed", desktop, StringComparison.Ordinal);
		Assert.Contains("Install one from a .mokaplugin file", desktop, StringComparison.Ordinal);
		Assert.Contains("No plugins installed", web, StringComparison.Ordinal);
		Assert.Contains("The operator has not put any plugins in the plugins folder.", web, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_WithoutAFilePicker_OffersNoInstall()
	{
		string html = await RenderPageAsync(new FakePluginManager(), withPicker: false);

		Assert.DoesNotContain("Install from file", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task InstallDialog_ForACodePlugin_ShowsTheWarningEveryFileAndTheTrustTick()
	{
		FakePluginPackage package = new()
		{
			Id = "moka.proxmox",
			Name = "Proxmox",
			Version = "1.1.0",
			Publisher = "Moka",
			HasCode = true,
			Hosts = [PluginHosts.Desktop, PluginHosts.Web],
			Files =
			[
				new PluginPackageFile("lib/Mokaterm.Plugins.Proxmox.dll", 48_128, Sha),
				new PluginPackageFile("plugin.json", 212, new string('a', 64)),
			],
			Hash = new string('b', 64),
		};

		string html = await RenderDialogAsync(package);

		Assert.Contains("This plugin runs code", html, StringComparison.Ordinal);
		Assert.Contains("including the vault while it is unlocked and every open session", html, StringComparison.Ordinal);
		Assert.Contains("lib/Mokaterm.Plugins.Proxmox.dll", html, StringComparison.Ordinal);
		Assert.Contains(Sha, html, StringComparison.Ordinal);
		Assert.Contains(">2 files<", html, StringComparison.Ordinal);
		Assert.Contains(new string('b', 64), html, StringComparison.Ordinal);
		Assert.Contains("I trust this publisher", html, StringComparison.Ordinal);
		Assert.Contains("type=\"checkbox\"", html, StringComparison.Ordinal);
		Assert.Contains("Install turned off", html, StringComparison.Ordinal);

		// Turning code on waits for the tick; installing it turned off does not.
		Assert.True(IsDisabled(ButtonWithText(html, "Install and turn on")));
		Assert.False(IsDisabled(ButtonWithText(html, "Install turned off")));

		// The render is over and the dialog went with it, so the package it held in memory is let go.
		Assert.True(package.IsDisposed);
	}

	[Fact]
	public async Task InstallDialog_ForAnUpdate_SaysWhichVersionItReplaces()
	{
		string html = await RenderDialogAsync(new FakePluginPackage
		{
			Id = "moka.proxmox",
			Name = "Proxmox",
			Version = "1.1.0",
			InstalledVersion = "1.0.0",
		});

		Assert.Contains("Updates 1.0.0 to 1.1.0.", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task InstallDialog_WithAProblem_InstallsNothing()
	{
		string html = await RenderDialogAsync(new FakePluginPackage
		{
			Id = "acme.web-only",
			Name = "Web only",
			HasCode = true,
			Hosts = [PluginHosts.Web],
			Problem = "This plugin is only for the web host.",
		});

		Assert.Contains("This plugin is only for the web host.", html, StringComparison.Ordinal);
		Assert.DoesNotContain("I trust", html, StringComparison.Ordinal);
		Assert.True(IsDisabled(ButtonWithText(html, "Install turned off")));
		Assert.True(IsDisabled(ButtonWithText(html, "Install and turn on")));
	}

	[Fact]
	public async Task InstallDialog_ForADataPlugin_HasOneButtonAndNoWarning()
	{
		string html = await RenderDialogAsync(new FakePluginPackage
		{
			Id = "moka.themes",
			Name = "Night themes",
			ThemeCount = 4,
		});

		Assert.Contains(">DATA<", html, StringComparison.Ordinal);
		Assert.Contains("4 themes", html, StringComparison.Ordinal);
		Assert.DoesNotContain("This plugin runs code", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Install turned off", html, StringComparison.Ordinal);
		Assert.False(IsDisabled(ButtonWithText(html, "Install")));
	}

	[Theory]
	[InlineData(null, "1.0.0", null)]
	[InlineData("1.0.0", "1.1.0", "Updates 1.0.0 to 1.1.0.")]
	[InlineData("1.2.0", "1.1.0", "Goes back from 1.2.0 to 1.1.0.")]
	[InlineData("1.0.0", "1.0.0", "Version 1.0.0 is already installed. Installing replaces its files.")]
	[InlineData("1.0.0-beta", "1.0.0", "Updates 1.0.0-beta to 1.0.0.")]
	public void VersionChange_SaysWhatAnInstallDoesToTheInstalledCopy(string? installed, string version, string? expected) =>
		Assert.Equal(expected, PluginDisplay.VersionChange(installed, version));

	private static PluginRecord Plugin(string id, string name, PluginStatus status, bool hasCode, bool enabled) => new()
	{
		Id = id,
		Name = name,
		Version = "1.0.0",
		HasCode = hasCode,
		Status = status,
		IsEnabled = enabled,
	};

	private static Task<string> RenderPageAsync(FakePluginManager plugins, bool withPicker = true) =>
		StaticRender.RenderAsync<PluginsPage>(
			services =>
			{
				AddServices(services, plugins);
				if (withPicker)
				{
					services.AddSingleton<ILocalFileAccess, NoLocalFiles>();
				}
			},
			new Dictionary<string, object?>(StringComparer.Ordinal))
			.WaitAsync(TimeSpan.FromSeconds(10), Ct);

	private static Task<string> RenderDialogAsync(FakePluginPackage package) =>
		StaticRender.RenderAsync<PluginInstallDialog>(
			services => AddServices(services, new FakePluginManager()),
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				[nameof(PluginInstallDialog.Open)] = true,
				[nameof(PluginInstallDialog.OpenChanged)] = EventCallback.Factory.Create<bool>(new object(), static _ => { }),
				[nameof(PluginInstallDialog.Package)] = package,
			})
			.WaitAsync(TimeSpan.FromSeconds(10), Ct);

	private static void AddServices(IServiceCollection services, FakePluginManager plugins)
	{
		services.AddMokaRed();
		services.AddMokatermUiCommon();
		services.AddSingleton(TimeProvider.System);
		services.AddSingleton<ISettingsService>(new FixedSettings());
		services.AddSingleton<IUserInteraction>(new DismissingInteraction());
		services.AddSingleton<IPluginManager>(plugins);
	}

	/// <summary>The opening tag of the button the page names with <paramref name="label"/>.</summary>
	private static string Button(string html, string label)
	{
		Match match = Regex.Match(html, $"<button[^>]*aria-label=\"{Regex.Escape(label)}\"[^>]*>", RegexOptions.None, TimeSpan.FromSeconds(1));
		Assert.True(match.Success, $"No button named {label}.");
		return match.Value;
	}

	/// <summary>The opening tag of the button whose own text is <paramref name="text"/>.</summary>
	private static string ButtonWithText(string html, string text)
	{
		Match match = Regex.Match(
			html,
			$"(<button[^>]*>)(?:(?!</button>).)*?>\\s*{Regex.Escape(text)}\\s*<",
			RegexOptions.Singleline,
			TimeSpan.FromSeconds(1));
		Assert.True(match.Success, $"No button says {text}.");
		return match.Groups[1].Value;
	}

	private static bool IsDisabled(string openingTag) => DisabledAttribute().IsMatch(openingTag);

	[GeneratedRegex("\\sdisabled(?:[\\s=>/]|$)")]
	private static partial Regex DisabledAttribute();
}
