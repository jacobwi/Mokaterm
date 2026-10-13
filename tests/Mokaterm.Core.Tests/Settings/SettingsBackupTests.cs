using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Storage;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Core.Settings;
using Mokaterm.Core.Tests.TestSupport;

namespace Mokaterm.Core.Tests.Settings;

public sealed class SettingsBackupTests
{
	private static readonly string LogFolder = Path.Combine(Path.GetTempPath(), "mokaterm-logs");

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ExportThenImportOnAnotherMachine_BringsTheSettings_AndThatMachineKeepsItsOwnFoldersAndFeed()
	{
		byte[] file;
		await using (Machine first = new())
		{
			await first.Settings.UpdateAsync<TerminalSettings>(s => s with { FontSize = 16, RightClickAction = TerminalRightClickAction.CopyOrPaste }, Ct);
			await first.Settings.UpdateAsync<FileTransferSettings>(s => s with { MaxConcurrentTransfers = 5, DownloadDirectory = "first-downloads" }, Ct);
			await first.Settings.UpdateAsync<UpdateSettings>(s => s with { Feed = "https://first.example.com/feed", CheckAtStartup = false }, Ct);
			await first.Settings.UpdateAsync<SessionLogSettings>(s => s with { Folder = LogFolder, RecordEverySession = true }, Ct);
			file = (await first.Backup.ExportAsync(Ct)).Content;
		}

		await using Machine second = new();
		await second.Settings.UpdateAsync<FileTransferSettings>(s => s with { DownloadDirectory = "second-downloads" }, Ct);
		await second.Settings.UpdateAsync<UpdateSettings>(s => s with { Feed = "https://second.example.com/feed" }, Ct);

		await second.Backup.ImportAsync("settings.json", file, Ct);

		Assert.Equal(16, second.Settings.Get<TerminalSettings>().FontSize);
		Assert.Equal(TerminalRightClickAction.CopyOrPaste, second.Settings.Get<TerminalSettings>().RightClickAction);
		Assert.Equal(5, second.Settings.Get<FileTransferSettings>().MaxConcurrentTransfers);
		Assert.False(second.Settings.Get<UpdateSettings>().CheckAtStartup);
		Assert.True(second.Settings.Get<SessionLogSettings>().RecordEverySession);

		Assert.Equal("second-downloads", second.Settings.Get<FileTransferSettings>().DownloadDirectory);
		Assert.Equal("https://second.example.com/feed", second.Settings.Get<UpdateSettings>().Feed);
		Assert.Equal("", second.Settings.Get<SessionLogSettings>().Folder);
	}

	// None of the tool window switches names anything of this machine, so all four travel with the rest.
	[Fact]
	public async Task TheToolWindowSwitches_RoundTrip()
	{
		byte[] file;
		await using (Machine first = new())
		{
			await first.Settings.UpdateAsync<AppearanceSettings>(
				s => s with { ToolsInOwnWindow = false, ReopenToolWindows = false, RememberToolWindowPlaces = false, ShowToolDescriptions = true },
				Ct);
			file = (await first.Backup.ExportAsync(Ct)).Content;
		}

		await using Machine second = new();
		await second.Backup.ImportAsync("settings.json", file, Ct);

		AppearanceSettings imported = second.Settings.Get<AppearanceSettings>();
		Assert.False(imported.ToolsInOwnWindow);
		Assert.False(imported.ReopenToolWindows);
		Assert.False(imported.RememberToolWindowPlaces);
		Assert.True(imported.ShowToolDescriptions);
	}

	[Fact]
	public async Task Export_LeavesOutTheValuesThatOnlyFitThisMachine()
	{
		await using Machine machine = new();
		await machine.Settings.UpdateAsync<FileTransferSettings>(s => s with { ShowHiddenFiles = true, DownloadDirectory = "downloads" }, Ct);
		await machine.Settings.UpdateAsync<SessionLogSettings>(s => s with { Folder = LogFolder }, Ct);
		await machine.Settings.UpdateAsync<UpdateSettings>(s => s with { Feed = "https://example.com/feed" }, Ct);

		SettingsBackupFile export = await machine.Backup.ExportAsync(Ct);
		JsonObject settings = (JsonObject)JsonNode.Parse(export.Content)!["settings"]!;

		Assert.True((bool)settings["files"]!["showHiddenFiles"]!);
		Assert.False(((JsonObject)settings["files"]!).ContainsKey("downloadDirectory"));
		Assert.False(((JsonObject)settings["sessionlog"]!).ContainsKey("folder"));
		Assert.False(((JsonObject)settings["updates"]!).ContainsKey("feed"));
		Assert.Equal(3, export.Sections);
	}

	[Fact]
	public async Task Export_NamesItsFormatAndWhenItWasMade()
	{
		await using Machine machine = new();
		await machine.Settings.UpdateAsync<GeneralSettings>(s => s with { AutoReconnect = true }, Ct);

		SettingsBackupFile export = await machine.Backup.ExportAsync(Ct);
		SettingsBackupCheck check = machine.Backup.Check(export.FileName, export.Content);

		Assert.Equal("mokaterm-settings-2026-09-17.json", export.FileName);
		Assert.Equal(SettingsBackup.FileKind, (string?)JsonNode.Parse(export.Content)!["kind"]);
		Assert.True(check.CanImport);
		Assert.Equal(machine.Context.Time.GetUtcNow(), check.ExportedAt);
		Assert.Equal(1, check.Sections);
	}

	[Fact]
	public async Task Import_ASectionTheFileDoesNotHold_GoesBackToItsDefaults()
	{
		await using Machine machine = new();
		await machine.Settings.UpdateAsync<GeneralSettings>(s => s with { AutoReconnect = true }, Ct);

		await machine.Backup.ImportAsync("terminal-only.json", BackupOf("""{ "terminal": { "fontSize": 15 } }"""), Ct);

		Assert.Equal(new GeneralSettings(), machine.Settings.Get<GeneralSettings>());
		Assert.Equal(15, machine.Settings.Get<TerminalSettings>().FontSize);
	}

	[Fact]
	public async Task Import_GoesThroughTheSameChecksAsSettingsJson()
	{
		await using Machine machine = new();

		await machine.Backup.ImportAsync("edited.json", BackupOf("""{ "terminal": { "fontSize": 400, "terminalType": "  " } }"""), Ct);

		TerminalSettings terminal = machine.Settings.Get<TerminalSettings>();
		Assert.Equal(TerminalSettings.MaxFontSize, terminal.FontSize);
		Assert.Equal(new TerminalSettings().TerminalType, terminal.TerminalType);
	}

	[Fact]
	public async Task Import_KeepsTheSectionOfAModuleThisHostDoesNotLoad()
	{
		await using Machine machine = new();

		await machine.Backup.ImportAsync("module.json", BackupOf("""{ "test-module": { "keepAliveSeconds": 42 } }"""), Ct);

		Assert.Equal(42, machine.Settings.Get<SettingsServiceTests.ModuleSettings>().KeepAliveSeconds);
	}

	[Fact]
	public async Task Import_TellsEverySectionAndIsWrittenAtOnce()
	{
		await using Machine machine = new();
		await machine.Settings.UpdateAsync<GeneralSettings>(s => s with { AutoReconnect = true }, Ct);
		await machine.Settings.FlushAsync(Ct);
		int writes = machine.Store.WriteCount;
		List<string> changed = [];
		machine.Settings.Changed += changed.Add;

		await machine.Backup.ImportAsync("terminal-only.json", BackupOf("""{ "terminal": { "fontSize": 15 } }"""), Ct);

		Assert.Contains(GeneralSettings.SectionKey, changed);
		Assert.Contains(TerminalSettings.SectionKey, changed);
		Assert.Equal(writes + 1, machine.Store.WriteCount);
		Assert.Equal(15, (double)JsonNode.Parse(machine.Store.GetJson(SettingsService.DocumentName)!)!["terminal"]!["fontSize"]!);
	}

	[Theory]
	[InlineData("not json at all", "x.json is not readable JSON.")]
	[InlineData("""[ 1, 2 ]""", "x.json is not a Mokaterm settings file.")]
	[InlineData("""{ "kind": "mokaterm-connections", "version": 1, "settings": {} }""", "x.json is not a Mokaterm settings file.")]
	[InlineData("""{ "kind": "mokaterm-settings", "settings": {} }""", "x.json is not a Mokaterm settings file.")]
	[InlineData("""{ "kind": "mokaterm-settings", "version": 2, "settings": {} }""", "x.json comes from a newer version of Mokaterm.")]
	[InlineData("""{ "kind": "mokaterm-settings", "version": 1, "settings": { "terminal": { "fontSize": "large" } } }""", "The terminal settings in x.json could not be read.")]
	public async Task Check_RefusesWhatItCannotImport_AndImportChangesNothing(string content, string error)
	{
		await using Machine machine = new();
		await machine.Settings.UpdateAsync<GeneralSettings>(s => s with { AutoReconnect = true }, Ct);
		byte[] bytes = Encoding.UTF8.GetBytes(content);

		Assert.Equal(error, machine.Backup.Check("x.json", bytes).Error);
		InvalidDataException refused = await Assert.ThrowsAsync<InvalidDataException>(() => machine.Backup.ImportAsync("x.json", bytes, Ct));
		Assert.Equal(error, refused.Message);
		Assert.True(machine.Settings.Get<GeneralSettings>().AutoReconnect);
	}

	[Fact]
	public async Task Check_TakesAFileThatStartsWithAByteOrderMark()
	{
		await using Machine machine = new();
		byte[] file = [0xEF, 0xBB, 0xBF, .. BackupOf("""{ "general": { "autoReconnect": true } }""")];

		Assert.True(machine.Backup.Check("bom.json", file).CanImport);
	}

	private static byte[] BackupOf(string settings) =>
		Encoding.UTF8.GetBytes($$"""{ "kind": "mokaterm-settings", "version": 1, "settings": {{settings}} }""");

	/// <summary>One installation: its own settings document, and the section types its pages would register.</summary>
	private sealed class Machine : IAsyncDisposable
	{
		public Machine()
		{
			Context = new CoreTestContext(services =>
			{
				services.AddSingleton<IAppDataStore>(Store);
				services.AddSingleton(SettingsSectionType.Of<GeneralSettings>());
				services.AddSingleton(SettingsSectionType.Of<TerminalSettings>());
				services.AddSingleton(SettingsSectionType.Of<FileTransferSettings>());
				services.AddSingleton(SettingsSectionType.Of<SessionLogSettings>());
				services.AddSingleton(SettingsSectionType.Of<UpdateSettings>());
				services.AddSingleton(SettingsSectionType.Of<AppearanceSettings>());

				// Registered twice, the way a page and a module could both register one section.
				services.AddSingleton(SettingsSectionType.Of<TerminalSettings>());
			});
		}

		public InMemoryAppDataStore Store { get; } = new();

		public CoreTestContext Context { get; }

		public ISettingsService Settings => Context.Services.GetRequiredService<ISettingsService>();

		public ISettingsBackup Backup => Context.Services.GetRequiredService<ISettingsBackup>();

		public ValueTask DisposeAsync() => Context.DisposeAsync();
	}
}
