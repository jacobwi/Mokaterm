using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Editing;
using Mokaterm.UI.FileBrowser.Dialogs;
using Mokaterm.UI.FileBrowser.Editing;
using Mokaterm.UI.FileBrowser.Extensions;
using Mokaterm.UI.FileBrowser.Tests.Fakes;

namespace Mokaterm.UI.FileBrowser.Tests;

/// <summary>
/// The one editing flow behind the file browser and every module: what opens, what is refused with a notice, what comes
/// back to the caller unsaid, and that a save from the dialog writes the file in its own format.
/// </summary>
public sealed class RemoteTextEditorTests
{
	private const string FilePath = "/srv/shop/.env";

	private readonly TextFileSystem _server = new();
	private readonly ScriptedDialogs _dialogs = new();
	private readonly RecordingInteraction _interaction = new();
	private readonly FakeSettingsService _settings = new();

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task TheFileOpensWithItsFacts_AndASaveFromTheEditorWritesItInItsOwnFormat()
	{
		_server.WriteElsewhere(FilePath, "A=1\r\n");
		_dialogs.Answer = async dialog =>
		{
			FileSaveResult saved = await dialog.Parameter<Func<string, bool, Task<FileSaveResult>>>(nameof(FileEditDialog.Save))("A=2\n", false);
			Assert.Equal(FileSaveOutcome.Saved, saved.Outcome);
			return true;
		};

		RemoteTextEditResult result = await EditAsync(new RemoteTextEditRequest { FileSystem = _server, Path = FilePath });

		Assert.Equal(RemoteTextEditOutcome.Saved, result.Outcome);
		Assert.Equal("A=2\r\n", _server.Read(FilePath));
		ScriptedDialogs.ShownDialog dialog = Assert.Single(_dialogs.Shown);
		Assert.Equal(typeof(FileEditDialog), dialog.Component);
		Assert.Equal(".env", dialog.Title);
		Assert.Equal("A=1\n", dialog.Parameter<string>(nameof(FileEditDialog.Content)));
		Assert.Equal(["5 B", "UTF-8", "CRLF"], dialog.Parameter<IReadOnlyList<string>>(nameof(FileEditDialog.Facts)));
		Assert.Empty(_interaction.Notices);
	}

	[Fact]
	public async Task TheDisplayName_TitlesTheEditor()
	{
		_server.WriteElsewhere(FilePath, "A=1\n");

		await EditAsync(new RemoteTextEditRequest { FileSystem = _server, Path = FilePath, DisplayName = "shop .env" });

		Assert.Equal("shop .env", Assert.Single(_dialogs.Shown).Title);
	}

	[Fact]
	public async Task ClosingTheEditor_WritesNothing()
	{
		_server.WriteElsewhere(FilePath, "A=1\n");

		RemoteTextEditResult result = await EditAsync(new RemoteTextEditRequest { FileSystem = _server, Path = FilePath });

		Assert.Equal(RemoteTextEditOutcome.Closed, result.Outcome);
		Assert.Empty(_server.Uploads);
	}

	[Fact]
	public async Task DiscardingEdits_AsksFirst()
	{
		_server.WriteElsewhere(FilePath, "A=1\n");
		_interaction.ConfirmAnswer = new ConfirmResult(true, false);
		_dialogs.Answer = async dialog =>
		{
			Assert.True(await dialog.Parameter<Func<Task<bool>>>(nameof(FileEditDialog.ConfirmDiscard))());
			return null;
		};

		await EditAsync(new RemoteTextEditRequest { FileSystem = _server, Path = FilePath });

		ConfirmPrompt asked = Assert.Single(_interaction.Confirms);
		Assert.Equal("Discard changes", asked.Title);
		Assert.True(asked.Destructive);
	}

	[Fact]
	public async Task AFileOverTheSettingsCap_IsRefusedWithANotice_AndNeverOpened()
	{
		_settings.Set(new FileTransferSettings { MaxEditKilobytes = FileTransferSettings.MinEditKilobytes });
		_server.WriteElsewhere(FilePath, new string('a', (FileTransferSettings.MinEditKilobytes * 1024) + 1));

		RemoteTextEditResult result = await EditAsync(new RemoteTextEditRequest { FileSystem = _server, Path = FilePath });

		Assert.Equal(RemoteTextEditOutcome.Refused, result.Outcome);
		Assert.Equal(TextFileRefusal.TooLarge, result.Refusal);
		Notice notice = Assert.Single(_interaction.Notices);
		Assert.Equal(NoticeSeverity.Warning, notice.Severity);
		Assert.Equal("Cannot edit .env", notice.Title);
		Assert.Contains("16 KB", notice.Message, StringComparison.Ordinal);
		Assert.Empty(_dialogs.Shown);
	}

	[Fact]
	public async Task AFileAtTheCap_StillOpens()
	{
		_settings.Set(new FileTransferSettings { MaxEditKilobytes = FileTransferSettings.MinEditKilobytes });
		_server.WriteElsewhere(FilePath, new string('a', FileTransferSettings.MinEditKilobytes * 1024));

		RemoteTextEditResult result = await EditAsync(new RemoteTextEditRequest { FileSystem = _server, Path = FilePath });

		Assert.Equal(RemoteTextEditOutcome.Closed, result.Outcome);
		Assert.Single(_dialogs.Shown);
	}

	[Fact]
	public async Task ABinaryFile_IsRefused()
	{
		_server.WriteElsewhere(FilePath, [0x41, 0x00, 0x42]);

		RemoteTextEditResult result = await EditAsync(new RemoteTextEditRequest { FileSystem = _server, Path = FilePath });

		Assert.Equal(RemoteTextEditOutcome.Refused, result.Outcome);
		Assert.Equal(TextFileRefusal.Binary, result.Refusal);
		Assert.Single(_interaction.Notices);
		Assert.Empty(_dialogs.Shown);
	}

	[Fact]
	public async Task AFileTheLoginMayNotRead_ComesBackToTheCaller_WithNothingShown()
	{
		_server.WriteElsewhere(FilePath, "A=1\n");
		_server.ReadFailure = new RemoteFileSystemException(RemoteFileErrorKind.PermissionDenied, "denied", FilePath);

		RemoteTextEditResult result = await EditAsync(new RemoteTextEditRequest { FileSystem = _server, Path = FilePath });

		Assert.Equal(RemoteTextEditOutcome.Failed, result.Outcome);
		Assert.True(result.IsPermissionDenied);
		Assert.Equal("Permission denied.", result.Message);
		Assert.Empty(_interaction.Notices);
		Assert.Empty(_dialogs.Shown);
	}

	[Fact]
	public async Task CreateNew_CreatesTheFileEmpty_AndOpensIt()
	{
		RemoteTextEditResult result = await EditAsync(new RemoteTextEditRequest { FileSystem = _server, Path = FilePath, CreateNew = true });

		Assert.Equal(RemoteTextEditOutcome.Closed, result.Outcome);
		Assert.Equal("", _server.Read(FilePath));
		Assert.Equal("", Assert.Single(_dialogs.Shown).Parameter<string>(nameof(FileEditDialog.Content)));
	}

	[Fact]
	public async Task CreateNew_ThenSave_EndsTheNewFileWithANewline()
	{
		_dialogs.Answer = async dialog =>
		{
			await dialog.Parameter<Func<string, bool, Task<FileSaveResult>>>(nameof(FileEditDialog.Save))("A=1", false);
			return true;
		};

		RemoteTextEditResult result = await EditAsync(new RemoteTextEditRequest { FileSystem = _server, Path = FilePath, CreateNew = true });

		Assert.Equal(RemoteTextEditOutcome.Saved, result.Outcome);
		Assert.Equal("A=1\n", _server.Read(FilePath));
	}

	[Fact]
	public async Task CreateNew_RefusesANameThatIsTaken_AndLeavesTheFileAlone()
	{
		_server.WriteElsewhere(FilePath, "A=1\n");

		RemoteTextEditResult result = await EditAsync(new RemoteTextEditRequest { FileSystem = _server, Path = FilePath, CreateNew = true });

		Assert.Equal(RemoteTextEditOutcome.Failed, result.Outcome);
		Assert.Equal(RemoteFileErrorKind.AlreadyExists, Assert.IsType<RemoteFileSystemException>(result.Error).Kind);
		Assert.Equal("A=1\n", _server.Read(FilePath));
		Assert.Empty(_dialogs.Shown);
	}

	[Fact]
	public async Task ACancelledEdit_EndsQuietly()
	{
		_server.WriteElsewhere(FilePath, "A=1\n");
		using CancellationTokenSource cancelled = new();
		await cancelled.CancelAsync();

		RemoteTextEditResult result = await Editor().EditAsync(new RemoteTextEditRequest { FileSystem = _server, Path = FilePath }, cancelled.Token);

		Assert.Equal(RemoteTextEditOutcome.Closed, result.Outcome);
		Assert.Empty(_dialogs.Shown);
		Assert.Empty(_interaction.Notices);
	}

	[Fact]
	public void TheFileBrowser_RegistersTheEditorPerPage()
	{
		ServiceCollection services = new();
		services.AddMokatermFileBrowser();

		ServiceDescriptor editor = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRemoteTextEditor));
		Assert.Equal(ServiceLifetime.Scoped, editor.Lifetime);
		Assert.Equal(typeof(RemoteTextEditor), editor.ImplementationType);
	}

	private RemoteTextEditor Editor() => new(_dialogs, _interaction, _settings);

	private Task<RemoteTextEditResult> EditAsync(RemoteTextEditRequest request) =>
		Editor().EditAsync(request, Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);
}
