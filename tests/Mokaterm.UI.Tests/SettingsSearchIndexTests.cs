using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Plugins;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Settings.Extensions;
using Mokaterm.UI.Settings.Pages;
using Mokaterm.UI.Settings.Pages.Updates;
using Mokaterm.UI.Terminal.Triggers;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The settings search index is read from the pages' markup at build time and the pages render from the same markup,
/// so every row a built-in page draws has to be in its index. A row that is not is one the scanner misread, or one
/// whose label is computed and that should say Searchable="false".
/// </summary>
public sealed partial class SettingsSearchIndexTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	public static TheoryData<Type> BuiltInPages()
	{
		ServiceCollection services = new();
		services.AddMokatermSettingsPages();
		TheoryData<Type> pages = [];
		foreach (Type component in services
			.Select(service => service.ImplementationInstance)
			.OfType<SettingsPageDescriptor>()
			.Select(page => page.Component)
			.Where(component => !IsVaultList(component)))
		{
			pages.Add(component);
		}

		// Registered by the desktop host next to its updater, so AddMokatermSettingsPages does not list it.
		pages.Add(typeof(UpdateSettingsPage));
		return pages;
	}

	[Theory]
	[MemberData(nameof(BuiltInPages))]
	public async Task EveryRowAPageRenders_IsInItsIndex(Type page)
	{
		string html = await RenderAsync(page);
		string[] rendered = [.. RowAnchor().Matches(html).Select(match => match.Groups[1].Value)];
		HashSet<string> indexed = [.. SettingsSearchIndex.For(page).Select(entry => entry.Anchor)];

		string[] unknown = [.. rendered.Where(anchor => !indexed.Contains(anchor))];
		Assert.True(unknown.Length == 0, $"{page.Name} draws rows the settings search does not know: {string.Join(", ", unknown)}.");
		Assert.Equal(indexed.Count > 0, rendered.Length > 0);
	}

	[Fact]
	public void Index_HoldsTheLabelAndDescriptionThePageWritesOut()
	{
		SettingsSearchEntry rightClick = Assert.Single(
			SettingsSearchIndex.For(typeof(Settings.Pages.Terminal.TerminalSettingsPage)),
			entry => entry.Label == "Right-click");

		Assert.Equal("Input and clipboard", rightClick.Section);
		Assert.StartsWith("What a right-click inside the terminal does.", rightClick.Description, StringComparison.Ordinal);
		Assert.Equal("right-click", rightClick.Anchor);
	}

	[Fact]
	public void Index_LeavesOutRowsThatAreNotSearchable()
	{
		IReadOnlyList<SettingsSearchEntry> about = SettingsSearchIndex.For(typeof(Settings.Pages.About.AboutPage));

		Assert.Contains(about, entry => entry.Label == "Data folder");
		Assert.DoesNotContain(about, entry => entry.Label == "No modules loaded");
	}

	[Fact]
	public void AddSettingsPage_ForAPageOnASection_RegistersTheSectionsType()
	{
		ServiceCollection services = new();
		services.AddMokatermSettingsPages();
		SettingsSectionType[] sections = [.. services.Select(service => service.ImplementationInstance).OfType<SettingsSectionType>()];

		Assert.Contains(SettingsSectionType.Of<TerminalSettings>(), sections);
		Assert.Contains(SettingsSectionType.Of<FileTransferSettings>(), sections);
		Assert.Contains(SettingsSectionType.Of<SessionLogSettings>(), sections);
	}

	[Fact]
	public void AddSettingsPage_ForAPageOnNoSection_RegistersNoSectionType()
	{
		ServiceCollection services = new();
		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = "about",
			Title = "About",
			Icon = Common.Icons.MokatermIcons.Modules,
			Component = typeof(Settings.Pages.About.AboutPage),
		});

		Assert.DoesNotContain(services, service => service.ImplementationInstance is SettingsSectionType);
	}

	private static bool IsVaultList(Type component)
	{
		for (Type? type = component; type is not null; type = type.BaseType)
		{
			if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(VaultListPageBase<>))
			{
				return true;
			}
		}

		return false;
	}

	private static Task<string> RenderAsync(Type page) =>
		StaticRender.RenderAsync<DynamicComponent>(
			services =>
			{
				services.AddMokaRed();
				services.AddMokatermUiCommon();
				services.AddScoped<TerminalTriggerTester>();
				services.AddSingleton(TimeProvider.System);
				services.AddSingleton<ISettingsService>(new FixedSettings());
				services.AddSingleton<IUserInteraction>(new DismissingInteraction());
				services.AddSingleton<IAppEnvironment>(new FakeAppEnvironment(Path.Combine("C:", "data")));
				services.AddSingleton<IVault, UnlockedVault>();
				services.AddSingleton<ISessionManager, ManualSessionManager>();
				services.AddSingleton<IProtocolRegistry, NoProtocols>();
				services.AddSingleton<ITerminalThemeCatalog>(new OneTheme("moka-dark", "Moka Dark"));
				services.AddSingleton<ITerminalThemeConverter, NoConverter>();
				services.AddSingleton<ILocalFileAccess, NoLocalFiles>();
				services.AddSingleton<ISettingsBackup, NoBackup>();
				services.AddSingleton<ISessionLogRecorder, NoRecorder>();
				services.AddSingleton<IPluginManager>(new FakePluginManager());
			},
			new Dictionary<string, object?>(StringComparer.Ordinal) { ["Type"] = page })
			.WaitAsync(TimeSpan.FromSeconds(10), Ct);

	[GeneratedRegex("data-setting=\"([^\"]*)\"")]
	private static partial Regex RowAnchor();

	private sealed class NoConverter : ITerminalThemeConverter
	{
		public TerminalThemeImport Import(string text, string? fileName = null) => throw new NotSupportedException();

		public string Export(TerminalTheme theme, TerminalThemeFormat format) => throw new NotSupportedException();
	}

	private sealed class NoBackup : ISettingsBackup
	{
		public Task<SettingsBackupFile> ExportAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public SettingsBackupCheck Check(string fileName, byte[] content) => throw new NotSupportedException();

		public Task ImportAsync(string fileName, byte[] content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
	}

	private sealed class NoRecorder : ISessionLogRecorder
	{
		public event Action? Changed
		{
			add { }
			remove { }
		}

		public string Folder => Path.Combine("C:", "logs");

		public SessionLogStatus StatusFor(Guid sessionId) => SessionLogStatus.Off;

		public void SetRecording(Guid sessionId, bool record)
		{
		}

		public IReadOnlyList<SessionLogFile> ListFiles() => [];

		public Stream OpenRead(string name) => throw new NotSupportedException();
	}
}
