using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.FileBrowser.Editing;
using Mokaterm.UI.FileBrowser.Tests.Fakes;

namespace Mokaterm.UI.FileBrowser.Tests;

/// <summary>
/// The save behind the editor. It writes in the file's own format, and when the file changed on the server since it was
/// read it answers with the server's copy instead of writing over it; going back to editing makes that copy the new
/// baseline, so only a further change stops the next save.
/// </summary>
public sealed class RemoteFileEditTests
{
	private const string FilePath = "/etc/app.conf";
	private const int MaxBytes = 1024;

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task AnUnchangedFile_IsWrittenInItsOwnFormat()
	{
		TextFileSystem server = new TextFileSystem().WriteElsewhere(FilePath, "one\r\ntwo\r\n");
		RemoteFileEdit edit = await OpenAsync(server);

		FileSaveResult result = await edit.SaveAsync("one\ntwo\nthree\n", overwrite: false, Ct);

		Assert.Equal(FileSaveOutcome.Saved, result.Outcome);
		Assert.Equal(["one\r\ntwo\r\nthree\r\n"], server.Uploads);
	}

	[Fact]
	public async Task AFileChangedOnTheServer_IsNotWritten_AndItsTextComesBack()
	{
		TextFileSystem server = new TextFileSystem().WriteElsewhere(FilePath, "port = 80\n");
		RemoteFileEdit edit = await OpenAsync(server);
		server.WriteElsewhere(FilePath, "port = 8080\r\n");

		FileSaveResult result = await edit.SaveAsync("port = 81\n", overwrite: false, Ct);

		Assert.Equal(FileSaveOutcome.Conflict, result.Outcome);
		Assert.Equal("port = 8080\n", result.ServerText);
		Assert.NotNull(result.ServerFacts);
		Assert.Equal(["UTF-8", "CRLF"], result.ServerFacts.Skip(1));
		Assert.Empty(server.Uploads);
		Assert.Equal("port = 8080\r\n", server.Read(FilePath));
	}

	[Fact]
	public async Task Overwrite_WritesOverTheServersChange()
	{
		TextFileSystem server = new TextFileSystem().WriteElsewhere(FilePath, "port = 80\n");
		RemoteFileEdit edit = await OpenAsync(server);
		server.WriteElsewhere(FilePath, "port = 8080\n");

		FileSaveResult result = await edit.SaveAsync("port = 81\n", overwrite: true, Ct);

		Assert.Equal(FileSaveOutcome.Saved, result.Outcome);
		Assert.Equal("port = 81\n", server.Read(FilePath));
	}

	[Fact]
	public async Task GoingBackToEditing_MakesTheServerCopyTheBaseline()
	{
		TextFileSystem server = new TextFileSystem().WriteElsewhere(FilePath, "a\n");
		RemoteFileEdit edit = await OpenAsync(server);
		server.WriteElsewhere(FilePath, "a\nb\n");
		Assert.Equal(FileSaveOutcome.Conflict, (await edit.SaveAsync("a\nc\n", overwrite: false, Ct)).Outcome);

		edit.AcceptServerCopy();
		FileSaveResult result = await edit.SaveAsync("a\nb\nc\n", overwrite: false, Ct);

		Assert.Equal(FileSaveOutcome.Saved, result.Outcome);
		Assert.Equal("a\nb\nc\n", server.Read(FilePath));
	}

	[Fact]
	public async Task AfterGoingBack_AFurtherChangeStopsTheSaveAgain()
	{
		TextFileSystem server = new TextFileSystem().WriteElsewhere(FilePath, "a\n");
		RemoteFileEdit edit = await OpenAsync(server);
		server.WriteElsewhere(FilePath, "a\nb\n");
		await edit.SaveAsync("a\nc\n", overwrite: false, Ct);
		edit.AcceptServerCopy();
		server.WriteElsewhere(FilePath, "a\nb\nd\n");

		FileSaveResult result = await edit.SaveAsync("a\nb\nc\n", overwrite: false, Ct);

		Assert.Equal(FileSaveOutcome.Conflict, result.Outcome);
		Assert.Equal("a\nb\nd\n", result.ServerText);
		Assert.Empty(server.Uploads);
	}

	[Fact]
	public async Task GoingBack_WritesInTheFormatTheServerCopyHas()
	{
		TextFileSystem server = new TextFileSystem().WriteElsewhere(FilePath, "a\nb\n");
		RemoteFileEdit edit = await OpenAsync(server);

		// Someone converted the file to CRLF and dropped its final newline meanwhile.
		server.WriteElsewhere(FilePath, "a\r\nb");
		await edit.SaveAsync("a\nb\nc\n", overwrite: false, Ct);
		edit.AcceptServerCopy();

		await edit.SaveAsync("a\nb\nc\n", overwrite: false, Ct);

		Assert.Equal(["a\r\nb\r\nc"], server.Uploads);
	}

	[Fact]
	public async Task AcceptingWithNoConflictWaiting_ChangesNothing()
	{
		TextFileSystem server = new TextFileSystem().WriteElsewhere(FilePath, "a\n");
		RemoteFileEdit edit = await OpenAsync(server);
		edit.AcceptServerCopy();
		server.WriteElsewhere(FilePath, "b\n");

		Assert.Equal(FileSaveOutcome.Conflict, (await edit.SaveAsync("c\n", overwrite: false, Ct)).Outcome);
	}

	[Fact]
	public async Task AFileDeletedOnTheServer_IsAConflictThatSaysSo_AndGoingBackLetsTheSaveRecreateIt()
	{
		TextFileSystem server = new TextFileSystem().WriteElsewhere(FilePath, "a\n");
		RemoteFileEdit edit = await OpenAsync(server);
		server.DeleteElsewhere(FilePath);

		FileSaveResult result = await edit.SaveAsync("b\n", overwrite: false, Ct);

		Assert.Equal(FileSaveOutcome.Conflict, result.Outcome);
		Assert.Null(result.ServerText);
		Assert.Contains("no longer on the server", result.Message, StringComparison.Ordinal);

		edit.AcceptServerCopy();
		Assert.Equal(FileSaveOutcome.Saved, (await edit.SaveAsync("b\n", overwrite: false, Ct)).Outcome);
		Assert.Equal("b\n", server.Read(FilePath));
	}

	[Theory]
	[InlineData(new byte[] { 0x61, 0x00, 0x62 }, "not text")]
	[InlineData(new byte[] { 0x63, 0x61, 0x66, 0xE9 }, "no longer UTF-8")]
	public async Task ACopyThatIsNoLongerText_IsAConflictThatSaysWhy(byte[] bytes, string reason)
	{
		TextFileSystem server = new TextFileSystem().WriteElsewhere(FilePath, "a\n");
		RemoteFileEdit edit = await OpenAsync(server);
		server.WriteElsewhere(FilePath, bytes);

		FileSaveResult result = await edit.SaveAsync("b\n", overwrite: false, Ct);

		Assert.Equal(FileSaveOutcome.Conflict, result.Outcome);
		Assert.Null(result.ServerText);
		Assert.Null(result.ServerFacts);
		Assert.Contains(reason, result.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ACopyGrownPastTheCap_IsAConflictThatSaysWhy_AndGoingBackKeepsTheOldFormat()
	{
		TextFileSystem server = new TextFileSystem().WriteElsewhere(FilePath, "a\r\n");
		RemoteFileEdit edit = await OpenAsync(server);
		server.WriteElsewhere(FilePath, new string('x', MaxBytes + 1));

		FileSaveResult result = await edit.SaveAsync("b\n", overwrite: false, Ct);

		Assert.Equal(FileSaveOutcome.Conflict, result.Outcome);
		Assert.Contains("bigger than", result.Message, StringComparison.Ordinal);

		// The stamp moved to the copy that could not be shown, so the next save goes through, still in CRLF.
		edit.AcceptServerCopy();
		Assert.Equal(FileSaveOutcome.Saved, (await edit.SaveAsync("b\n", overwrite: false, Ct)).Outcome);
		Assert.Equal(["b\r\n"], server.Uploads);
	}

	[Fact]
	public async Task ACopyThatCannotBeRead_IsAConflictThatSaysWhy()
	{
		TextFileSystem server = new TextFileSystem().WriteElsewhere(FilePath, "a\n");
		RemoteFileEdit edit = await OpenAsync(server);
		server.WriteElsewhere(FilePath, "b\n");
		server.ReadFailure = new RemoteFileSystemException(RemoteFileErrorKind.PermissionDenied, "denied", FilePath);

		FileSaveResult result = await edit.SaveAsync("c\n", overwrite: false, Ct);

		Assert.Equal(FileSaveOutcome.Conflict, result.Outcome);
		Assert.Null(result.ServerText);
		Assert.Contains("Permission denied.", result.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ARefusedWrite_IsAFailureWithTheServersReason()
	{
		TextFileSystem server = new TextFileSystem().WriteElsewhere(FilePath, "a\n");
		RemoteFileEdit edit = await OpenAsync(server);
		server.UploadFailures.Enqueue(new RemoteFileSystemException(RemoteFileErrorKind.PermissionDenied, "denied", FilePath));

		FileSaveResult result = await edit.SaveAsync("b\n", overwrite: false, Ct);

		Assert.Equal(FileSaveOutcome.Failed, result.Outcome);
		Assert.Equal("Permission denied.", result.Message);
		Assert.Equal("a\n", server.Read(FilePath));
	}

	[Fact]
	public async Task AFileSystemThatCannotStat_IsNeverAskedAgain()
	{
		TextFileSystem server = new TextFileSystem().WriteElsewhere(FilePath, "a\n");
		await using Stream source = await server.OpenReadAsync(FilePath, Ct);
		(RemoteTextFile? file, _) = await RemoteTextFile.ReadAsync(source, MaxBytes, Ct);
		RemoteFileEdit edit = new(server, FilePath, file!, stamp: null, MaxBytes);
		server.WriteElsewhere(FilePath, "changed\n");

		Assert.Equal(FileSaveOutcome.Saved, (await edit.SaveAsync("b\n", overwrite: false, Ct)).Outcome);
	}

	private static async Task<RemoteFileEdit> OpenAsync(TextFileSystem server)
	{
		await using Stream source = await server.OpenReadAsync(FilePath, Ct);
		(RemoteTextFile? file, _) = await RemoteTextFile.ReadAsync(source, MaxBytes, Ct);
		return new RemoteFileEdit(server, FilePath, file!, await server.StatAsync(FilePath, Ct), MaxBytes);
	}
}
