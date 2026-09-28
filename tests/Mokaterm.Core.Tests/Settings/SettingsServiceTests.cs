using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Storage;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Core.Settings;
using Mokaterm.Core.Tests.Terminal;
using Mokaterm.Core.Tests.TestSupport;

namespace Mokaterm.Core.Tests.Settings;

public sealed class SettingsServiceTests
{
	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Get_WithoutDocument_ReturnsDefaults()
	{
		(CoreTestContext context, _) = CreateInMemory();
		await using (context)
		{
			ISettingsService settings = Settings(context);
			await settings.LoadAsync(Ct);

			Assert.Equal(new TerminalSettings(), settings.Get<TerminalSettings>());
			Assert.Equal(new AppearanceSettings(), settings.Get<AppearanceSettings>());
			Assert.Same(settings.Get<SecuritySettings>(), settings.Get<SecuritySettings>());
		}
	}

	[Fact]
	public async Task UpdateAsync_ChangesSectionAndRaisesChanged()
	{
		(CoreTestContext context, _) = CreateInMemory();
		await using (context)
		{
			ISettingsService settings = Settings(context);
			List<string> changes = [];
			settings.Changed += changes.Add;

			await settings.UpdateAsync<TerminalSettings>(terminal => terminal with { FontSize = 16, ThemeId = "nord" }, Ct);

			Assert.Equal(16, settings.Get<TerminalSettings>().FontSize);
			Assert.Equal("nord", settings.Get<TerminalSettings>().ThemeId);
			Assert.Equal(TerminalSettings.SectionKey, Assert.Single(changes));
		}
	}

	[Fact]
	public async Task UpdateAsync_NoActualChange_DoesNotRaiseOrWrite()
	{
		(CoreTestContext context, InMemoryAppDataStore store) = CreateInMemory();
		await using (context)
		{
			ISettingsService settings = Settings(context);
			int changes = 0;
			settings.Changed += _ => changes++;

			await settings.UpdateAsync<GeneralSettings>(general => general with { }, Ct);
			await settings.FlushAsync(Ct);

			Assert.Equal(0, changes);
			Assert.Equal(0, store.WriteCount);
		}
	}

	[Fact]
	public async Task UpdateAsync_WritesOnceAfterDebounce()
	{
		(CoreTestContext context, InMemoryAppDataStore store) = CreateInMemory();
		await using (context)
		{
			ISettingsService settings = Settings(context);

			await settings.UpdateAsync<TerminalSettings>(terminal => terminal with { FontSize = 14 }, Ct);
			context.Time.Advance(TimeSpan.FromMilliseconds(300));
			await settings.UpdateAsync<TerminalSettings>(terminal => terminal with { FontSize = 15 }, Ct);
			context.Time.Advance(TimeSpan.FromMilliseconds(499));
			Assert.Equal(0, store.WriteCount);

			context.Time.Advance(TimeSpan.FromMilliseconds(1));

			Assert.Equal(1, store.WriteCount);
			Assert.Equal(15, (double)JsonNode.Parse(store.GetJson(SettingsService.DocumentName)!)!["terminal"]!["fontSize"]!);
		}
	}

	[Fact]
	public async Task FlushAsync_WritesPendingChangesImmediately()
	{
		(CoreTestContext context, InMemoryAppDataStore store) = CreateInMemory();
		await using (context)
		{
			ISettingsService settings = Settings(context);
			await settings.UpdateAsync<AppearanceSettings>(appearance => appearance with { FontScale = 1.2 }, Ct);

			await settings.FlushAsync(Ct);
			await settings.FlushAsync(Ct);

			Assert.Equal(1, store.WriteCount);
			context.Time.Advance(TimeSpan.FromSeconds(5));
			Assert.Equal(1, store.WriteCount);
		}
	}

	[Fact]
	public async Task LoadAsync_IsIdempotent()
	{
		(CoreTestContext context, InMemoryAppDataStore store) = CreateInMemory();
		await using (context)
		{
			ISettingsService settings = Settings(context);

			await Task.WhenAll(settings.LoadAsync(Ct), settings.LoadAsync(Ct));
			await settings.LoadAsync(Ct);

			Assert.Equal(1, store.ReadCount);
		}
	}

	[Fact]
	public async Task LoadAsync_RaisesChangedForSectionsReadBeforeLoading()
	{
		(CoreTestContext context, InMemoryAppDataStore store) = CreateInMemory();
		store.SetJson(SettingsService.DocumentName, """{ "security": { "autoLockMinutes": 5 } }""");
		await using (context)
		{
			ISettingsService settings = Settings(context);
			Assert.Equal(15, settings.Get<SecuritySettings>().AutoLockMinutes);
			List<string> changes = [];
			settings.Changed += changes.Add;

			await settings.LoadAsync(Ct);

			Assert.Equal(5, settings.Get<SecuritySettings>().AutoLockMinutes);
			Assert.Equal(SecuritySettings.SectionKey, Assert.Single(changes));
		}
	}

	[Fact]
	public async Task FlushAsync_PreservesSectionsTheAppDoesNotKnow()
	{
		await using CoreTestContext context = new();
		string path = Path.Combine(context.DataDirectory, "settings.json");
		await File.WriteAllTextAsync(path, """
			{
			  "ssh": { "keepAliveSeconds": 30, "compression": true },
			  "terminal": { "fontSize": 12 }
			}
			""", Ct);
		ISettingsService settings = Settings(context);
		await settings.LoadAsync(Ct);

		await settings.UpdateAsync<TerminalSettings>(terminal => terminal with { FontSize = 18 }, Ct);
		await settings.FlushAsync(Ct);

		JsonObject saved = JsonNode.Parse(await File.ReadAllTextAsync(path, Ct))!.AsObject();
		Assert.Equal(30, (int)saved["ssh"]!["keepAliveSeconds"]!);
		Assert.True((bool)saved["ssh"]!["compression"]!);
		Assert.Equal(18, (double)saved["terminal"]!["fontSize"]!);
	}

	[Fact]
	public async Task LoadAsync_SanitizesHandEditedValues()
	{
		(CoreTestContext context, InMemoryAppDataStore store) = CreateInMemory();
		store.SetJson(SettingsService.DocumentName, """
			{
			  "appearance": { "accentColor": "red", "fontScale": 9, "mode": 7 },
			  "terminal": {
			    "fontSize": 500, "lineHeight": 0.1, "scrollback": -3, "replayBufferKilobytes": 999999, "minimumContrastRatio": 99,
			    "fontFamily": "  ", "terminalType": null, "themeId": "",
			    "customThemes": [ null ]
			  },
			  "security": { "autoLockMinutes": -5, "clipboardClearSeconds": 100000 },
			  "files": { "maxConcurrentTransfers": 0 },
			  "general": { "autoReconnectDelaySeconds": 0, "recentConnectionsLimit": 99 },
			  "updates": { "feed": "updates.example.com" },
			  "sessionlog": { "folder": "logs", "sizeLimitKilobytes": 0, "format": 9 }
			}
			""");
		await using (context)
		{
			ISettingsService settings = Settings(context);
			await settings.LoadAsync(Ct);

			AppearanceSettings appearance = settings.Get<AppearanceSettings>();
			Assert.Equal("#ef5350", appearance.AccentColor);
			Assert.Equal(1.5, appearance.FontScale);
			Assert.Equal(ThemeMode.Dark, appearance.Mode);

			TerminalSettings terminal = settings.Get<TerminalSettings>();
			TerminalSettings defaults = new();
			Assert.Equal(48, terminal.FontSize);
			Assert.Equal(1, terminal.LineHeight);
			Assert.Equal(0, terminal.Scrollback);
			Assert.Equal(TerminalSettings.MaxReplayBufferKilobytes, terminal.ReplayBufferKilobytes);
			Assert.Equal(21, terminal.MinimumContrastRatio);
			Assert.Equal(defaults.FontFamily, terminal.FontFamily);
			Assert.Equal(defaults.TerminalType, terminal.TerminalType);
			Assert.Equal(defaults.ThemeId, terminal.ThemeId);
			Assert.Empty(terminal.CustomThemes);

			Assert.Equal(0, settings.Get<SecuritySettings>().AutoLockMinutes);
			Assert.Equal(600, settings.Get<SecuritySettings>().ClipboardClearSeconds);
			Assert.Equal(1, settings.Get<FileTransferSettings>().MaxConcurrentTransfers);
			Assert.Equal(1, settings.Get<GeneralSettings>().AutoReconnectDelaySeconds);
			Assert.Equal(50, settings.Get<GeneralSettings>().RecentConnectionsLimit);
			Assert.Equal("", settings.Get<UpdateSettings>().Feed);

			SessionLogSettings sessionLog = settings.Get<SessionLogSettings>();
			Assert.Equal("", sessionLog.Folder);
			Assert.Equal(SessionLogSettings.MinSizeLimitKilobytes, sessionLog.SizeLimitKilobytes);
			Assert.Equal(SessionLogFormat.PlainText, sessionLog.Format);
		}
	}

	[Theory]
	[InlineData("  https://updates.example.com/win/  ", "https://updates.example.com/win/")]
	[InlineData(@"\\build\releases", @"\\build\releases")]
	[InlineData("updates.example.com", "")]
	[InlineData("", "")]
	public async Task UpdateAsync_SanitizesTheUpdateFeed(string feed, string expected)
	{
		(CoreTestContext context, _) = CreateInMemory();
		await using (context)
		{
			ISettingsService settings = Settings(context);

			await settings.UpdateAsync<UpdateSettings>(updates => updates with { Feed = feed }, Ct);

			Assert.Equal(expected, settings.Get<UpdateSettings>().Feed);
		}
	}

	[Fact]
	public async Task UpdateSettings_RoundTripThroughTheFile()
	{
		await using CoreTestContext context = new();
		ISettingsService settings = Settings(context);

		await settings.UpdateAsync<UpdateSettings>(
			updates => updates with { Feed = "https://updates.example.com/win/", CheckAtStartup = false },
			Ct);
		await settings.FlushAsync(Ct);

		await using CoreTestContext reopened = new();
		File.Copy(Path.Combine(context.DataDirectory, "settings.json"), Path.Combine(reopened.DataDirectory, "settings.json"));
		ISettingsService reloaded = Settings(reopened);
		await reloaded.LoadAsync(Ct);

		Assert.Equal("https://updates.example.com/win/", reloaded.Get<UpdateSettings>().Feed);
		Assert.False(reloaded.Get<UpdateSettings>().CheckAtStartup);
	}

	[Theory]
	[InlineData(@"C:\logs\mokaterm", @"C:\logs\mokaterm")]
	[InlineData(@"  \\build\logs  ", @"\\build\logs")]
	[InlineData("logs", "")]
	[InlineData("C:\\logs\nmokaterm", "")]
	[InlineData("", "")]
	public async Task UpdateAsync_SanitizesTheSessionLogFolder(string folder, string expected)
	{
		(CoreTestContext context, _) = CreateInMemory();
		await using (context)
		{
			ISettingsService settings = Settings(context);

			await settings.UpdateAsync<SessionLogSettings>(logs => logs with { Folder = folder }, Ct);

			Assert.Equal(expected, settings.Get<SessionLogSettings>().Folder);
		}
	}

	[Fact]
	public async Task SessionLogSettings_RoundTripThroughTheFile()
	{
		await using CoreTestContext context = new();
		ISettingsService settings = Settings(context);

		await settings.UpdateAsync<SessionLogSettings>(
			logs => logs with { RecordEverySession = true, SizeLimitKilobytes = 2048, Format = SessionLogFormat.Raw },
			Ct);
		await settings.FlushAsync(Ct);

		await using CoreTestContext reopened = new();
		File.Copy(Path.Combine(context.DataDirectory, "settings.json"), Path.Combine(reopened.DataDirectory, "settings.json"));
		ISettingsService reloaded = Settings(reopened);
		await reloaded.LoadAsync(Ct);

		SessionLogSettings logSettings = reloaded.Get<SessionLogSettings>();
		Assert.True(logSettings.RecordEverySession);
		Assert.Equal(2048, logSettings.SizeLimitKilobytes);
		Assert.Equal(SessionLogFormat.Raw, logSettings.Format);
	}

	// A short color is expanded, not kept as written: the browser's color picker only understands #rrggbb and would
	// show black for #fff.
	[Theory]
	[InlineData("#fff", "#ffffff")]
	[InlineData("#A1b2C3", "#A1b2C3")]
	[InlineData("#12345", "#ef5350")]
	[InlineData("ef5350", "#ef5350")]
	[InlineData("#ef5350\n", "#ef5350")]
	public async Task UpdateAsync_SanitizesAccentColor(string accent, string expected)
	{
		(CoreTestContext context, _) = CreateInMemory();
		await using (context)
		{
			ISettingsService settings = Settings(context);

			await settings.UpdateAsync<AppearanceSettings>(appearance => appearance with { AccentColor = accent, FontScale = 0.1 }, Ct);

			Assert.Equal(expected, settings.Get<AppearanceSettings>().AccentColor);
			Assert.Equal(0.75, settings.Get<AppearanceSettings>().FontScale);
		}
	}

	// The settings page offers up to 16 MB; the sanitizer used to cut that to 8 MB the moment it was saved.
	[Fact]
	public async Task UpdateAsync_TheLargestReplayBufferThePageOffers_IsKept()
	{
		(CoreTestContext context, _) = CreateInMemory();
		await using (context)
		{
			ISettingsService settings = Settings(context);

			await settings.UpdateAsync<TerminalSettings>(terminal => terminal with { ReplayBufferKilobytes = 16_384 }, Ct);

			Assert.Equal(16_384, settings.Get<TerminalSettings>().ReplayBufferKilobytes);
		}
	}

	[Theory]
	[InlineData("'Fira Code'; color: red")]
	[InlineData("monospace} body { display: none")]
	[InlineData("url(https://example.com/x)")]
	[InlineData("mono\\62 ace")]
	[InlineData("mono\nspace")]
	[InlineData("<b>mono</b>")]
	public async Task UpdateAsync_AFontListThatCouldBreakOutOfTheStyle_FallsBackToTheDefault(string fontFamily)
	{
		(CoreTestContext context, _) = CreateInMemory();
		await using (context)
		{
			ISettingsService settings = Settings(context);

			await settings.UpdateAsync<TerminalSettings>(terminal => terminal with { FontFamily = fontFamily }, Ct);

			Assert.Equal(new TerminalSettings().FontFamily, settings.Get<TerminalSettings>().FontFamily);
		}
	}

	[Fact]
	public async Task UpdateAsync_APlainFontList_IsKept()
	{
		(CoreTestContext context, _) = CreateInMemory();
		await using (context)
		{
			ISettingsService settings = Settings(context);

			await settings.UpdateAsync<TerminalSettings>(terminal => terminal with { FontFamily = "'Fira Code', \"Iosevka Term\", monospace" }, Ct);

			Assert.Equal("'Fira Code', \"Iosevka Term\", monospace", settings.Get<TerminalSettings>().FontFamily);
		}
	}

	[Fact]
	public async Task Get_UnreadableSection_ReturnsDefaults()
	{
		(CoreTestContext context, InMemoryAppDataStore store) = CreateInMemory();
		store.SetJson(SettingsService.DocumentName, """{ "terminal": { "fontSize": "huge" }, "security": { "hostKeyPolicy": "Strict" } }""");
		await using (context)
		{
			ISettingsService settings = Settings(context);
			await settings.LoadAsync(Ct);

			Assert.Equal(new TerminalSettings(), settings.Get<TerminalSettings>());
			Assert.Equal(HostKeyPolicy.Strict, settings.Get<SecuritySettings>().HostKeyPolicy);
		}
	}

	[Fact]
	public async Task ResetAsync_RestoresDefaultsAndRemovesSection()
	{
		(CoreTestContext context, InMemoryAppDataStore store) = CreateInMemory();
		await using (context)
		{
			ISettingsService settings = Settings(context);
			await settings.UpdateAsync<GeneralSettings>(general => general with { AutoReconnect = true }, Ct);
			await settings.UpdateAsync<FileTransferSettings>(files => files with { ShowHiddenFiles = true }, Ct);
			List<string> changes = [];
			settings.Changed += changes.Add;

			await settings.ResetAsync<GeneralSettings>(Ct);
			await settings.FlushAsync(Ct);

			Assert.Equal(new GeneralSettings(), settings.Get<GeneralSettings>());
			Assert.True(settings.Get<FileTransferSettings>().ShowHiddenFiles);
			Assert.Equal(GeneralSettings.SectionKey, Assert.Single(changes));
			JsonObject saved = JsonNode.Parse(store.GetJson(SettingsService.DocumentName)!)!.AsObject();
			Assert.False(saved.ContainsKey("general"));
			Assert.True(saved.ContainsKey("files"));
		}
	}

	[Fact]
	public async Task ModuleSection_RoundTripsThroughTheSameService()
	{
		await using CoreTestContext context = new();
		ISettingsService settings = Settings(context);

		await settings.UpdateAsync<ModuleSettings>(module => module with { KeepAliveSeconds = 45 }, Ct);
		await settings.FlushAsync(Ct);

		await using CoreTestContext reopened = new();
		File.Copy(Path.Combine(context.DataDirectory, "settings.json"), Path.Combine(reopened.DataDirectory, "settings.json"));
		ISettingsService reloaded = Settings(reopened);
		await reloaded.LoadAsync(Ct);
		Assert.Equal(45, reloaded.Get<ModuleSettings>().KeepAliveSeconds);
	}

	[Fact]
	public async Task UpdateAsync_KeepsCompleteCustomThemesAndDropsDuplicates()
	{
		(CoreTestContext context, _) = CreateInMemory();
		await using (context)
		{
			ISettingsService settings = Settings(context);
			TerminalTheme theme = TerminalThemeCatalogTests.CustomTheme("mine");

			await settings.UpdateAsync<TerminalSettings>(terminal => terminal with { CustomThemes = [theme, theme with { Name = "Duplicate" }] }, Ct);

			Assert.Equal("mine", Assert.Single(settings.Get<TerminalSettings>().CustomThemes).Id);
		}
	}

	// Every range below used to be written twice, once in the sanitizer and once in the settings page, and three pairs
	// disagreed: a value at the edge of what a page offered was either pulled in on load or snapped the next time
	// anyone touched the control. Now the section owns the range and both read it, so the edges have to survive.
	[Fact]
	public async Task UpdateAsync_TheEdgesOfEveryRangeThePagesOffer_AreKept()
	{
		(CoreTestContext context, _) = CreateInMemory();
		await using (context)
		{
			ISettingsService settings = Settings(context);

			await settings.UpdateAsync<AppearanceSettings>(a => a with { FontScale = AppearanceSettings.MaxFontScale }, Ct);
			await settings.UpdateAsync<TerminalSettings>(
				t => t with
				{
					FontSize = TerminalSettings.MaxFontSize,
					LineHeight = TerminalSettings.MaxLineHeight,
					LetterSpacing = TerminalSettings.MaxLetterSpacing,
					Scrollback = TerminalSettings.MaxScrollback,
					MinimumContrastRatio = TerminalSettings.MaxContrastRatio,
					ReplayBufferKilobytes = TerminalSettings.MaxReplayBufferKilobytes,
				},
				Ct);
			await settings.UpdateAsync<SecuritySettings>(
				s => s with
				{
					AutoLockMinutes = SecuritySettings.MaxAutoLockMinutes,
					ClipboardClearSeconds = SecuritySettings.MaxClipboardClearSeconds,
				},
				Ct);
			await settings.UpdateAsync<FileTransferSettings>(
				f => f with { MaxConcurrentTransfers = FileTransferSettings.MaxParallelTransfers },
				Ct);
			await settings.UpdateAsync<GeneralSettings>(
				g => g with
				{
					AutoReconnectDelaySeconds = GeneralSettings.MaxAutoReconnectDelaySeconds,
					RecentConnectionsLimit = GeneralSettings.MaxRecentConnectionsLimit,
				},
				Ct);

			Assert.Equal(AppearanceSettings.MaxFontScale, settings.Get<AppearanceSettings>().FontScale);
			TerminalSettings terminal = settings.Get<TerminalSettings>();
			Assert.Equal(TerminalSettings.MaxFontSize, terminal.FontSize);
			Assert.Equal(TerminalSettings.MaxLineHeight, terminal.LineHeight);
			Assert.Equal(TerminalSettings.MaxLetterSpacing, terminal.LetterSpacing);
			Assert.Equal(TerminalSettings.MaxScrollback, terminal.Scrollback);
			Assert.Equal(TerminalSettings.MaxContrastRatio, terminal.MinimumContrastRatio);
			Assert.Equal(TerminalSettings.MaxReplayBufferKilobytes, terminal.ReplayBufferKilobytes);
			SecuritySettings security = settings.Get<SecuritySettings>();
			Assert.Equal(SecuritySettings.MaxAutoLockMinutes, security.AutoLockMinutes);
			Assert.Equal(SecuritySettings.MaxClipboardClearSeconds, security.ClipboardClearSeconds);
			Assert.Equal(FileTransferSettings.MaxParallelTransfers, settings.Get<FileTransferSettings>().MaxConcurrentTransfers);
			GeneralSettings general = settings.Get<GeneralSettings>();
			Assert.Equal(GeneralSettings.MaxAutoReconnectDelaySeconds, general.AutoReconnectDelaySeconds);
			Assert.Equal(GeneralSettings.MaxRecentConnectionsLimit, general.RecentConnectionsLimit);
		}
	}

	// The narrower of two ranges won wherever they disagreed, so a file holding what the old sanitizer allowed is
	// corrected on load instead of being kept and then snapped by the first slider move.
	[Fact]
	public async Task LoadAsync_ValuesTheOldSanitizerAllowedButNoPageOffered_ArePulledIn()
	{
		(CoreTestContext context, InMemoryAppDataStore store) = CreateInMemory();
		store.SetJson(SettingsService.DocumentName, """
			{ "terminal": { "lineHeight": 2.5, "scrollback": 200000 }, "appearance": { "fontScale": 0.8 } }
			""");
		await using (context)
		{
			ISettingsService settings = Settings(context);
			await settings.LoadAsync(Ct);

			Assert.Equal(TerminalSettings.MaxLineHeight, settings.Get<TerminalSettings>().LineHeight);
			Assert.Equal(TerminalSettings.MaxScrollback, settings.Get<TerminalSettings>().Scrollback);

			// The other way round for the font scale: the stored range was the wider one and the slider now offers it all.
			Assert.Equal(0.8, settings.Get<AppearanceSettings>().FontScale);
		}
	}

	private static (CoreTestContext Context, InMemoryAppDataStore Store) CreateInMemory()
	{
		InMemoryAppDataStore store = new();
		return (new CoreTestContext(services => services.AddSingleton<IAppDataStore>(store)), store);
	}

	private static ISettingsService Settings(CoreTestContext context) => context.Services.GetRequiredService<ISettingsService>();

	public sealed record ModuleSettings : ISettingsSection
	{
		public static string SectionKey => "test-module";

		public int KeepAliveSeconds { get; init; } = 15;
	}
}
