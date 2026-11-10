using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.FileBrowser.Details;
using Mokaterm.UI.FileBrowser.Tests.Fakes;

namespace Mokaterm.UI.FileBrowser.Tests;

/// <summary>
/// What the details pane reads for its preview, and how much: the start of a text file within its cap, an image whole
/// within its cap and not at all past it, and nothing of anything that is not a regular file.
/// </summary>
public sealed class EntryPreviewsTests
{
	private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

	private readonly TextFileSystem _files = new();

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData("notes.md", RemoteEntryKind.File, null, "Text")]
	[InlineData("Makefile", RemoteEntryKind.File, null, "Text")]
	[InlineData("chart.png", RemoteEntryKind.File, null, "Image")]
	[InlineData("PHOTO.JPG", RemoteEntryKind.File, null, "Image")]
	[InlineData("photo.jpeg", RemoteEntryKind.File, null, "Image")]
	[InlineData("anim.gif", RemoteEntryKind.File, null, "Image")]
	[InlineData("banner.webp", RemoteEntryKind.File, null, "Image")]
	[InlineData("badge.svg", RemoteEntryKind.File, null, "Image")]
	[InlineData(".png", RemoteEntryKind.File, null, "Text")]
	[InlineData("src", RemoteEntryKind.Directory, null, "None")]
	[InlineData("fifo", RemoteEntryKind.Other, null, "None")]
	[InlineData("latest.png", RemoteEntryKind.SymbolicLink, RemoteEntryKind.File, "Image")]
	[InlineData("current", RemoteEntryKind.SymbolicLink, RemoteEntryKind.Directory, "None")]
	[InlineData("tty", RemoteEntryKind.SymbolicLink, RemoteEntryKind.Other, "None")]
	[InlineData("broken.txt", RemoteEntryKind.SymbolicLink, null, "None")]
	public void KindOf_PreviewsRegularFilesAndLinksToThemOnly(string name, RemoteEntryKind kind, RemoteEntryKind? target, string expected)
	{
		RemoteFileEntry entry = new() { Name = name, Path = "/srv/" + name, Kind = kind, LinkTargetKind = target };

		Assert.Equal(Enum.Parse<EntryPreviewKind>(expected), EntryPreviews.KindOf(entry));
	}

	[Fact]
	public async Task Text_KeepsTheStart_AndNeverReadsPastTheCap()
	{
		string line = new('x', 99);
		string text = string.Concat(Enumerable.Repeat(line + "\n", 2000));
		_files.WriteElsewhere("/srv/big.log", text);

		EntryPreview preview = await LoadAsync(File("big.log", text.Length));

		Assert.Equal(EntryPreviewState.Text, preview.State);
		Assert.True(preview.IsCut);
		Assert.Equal(EntryPreviews.TextBytes, preview.Text!.Length);
		Assert.StartsWith(line + "\n", preview.Text, StringComparison.Ordinal);
		Assert.InRange(_files.BytesRead, EntryPreviews.TextBytes, EntryPreviews.TextBytes + 1);
	}

	[Fact]
	public async Task Text_ShortFile_IsWhole_WithEscapesTakenOutAndCrlfAsLineFeeds()
	{
		_files.WriteElsewhere("/srv/run.log", "\u001b[32mok\u001b[0m step one\r\nstep\ttwo\r\n");

		EntryPreview preview = await LoadAsync(File("run.log", 33));

		Assert.Equal(EntryPreviewState.Text, preview.State);
		Assert.False(preview.IsCut);
		Assert.Equal("ok step one\nstep\ttwo\n", preview.Text);
	}

	[Fact]
	public async Task Text_ThatHoldsANul_IsBinary_AndNotShown()
	{
		_files.WriteElsewhere("/srv/app.bin", [0x7F, 0x45, 0x4C, 0x46, 0x00, 0x01]);

		EntryPreview preview = await LoadAsync(File("app.bin", 6));

		Assert.Equal(EntryPreviewState.Binary, preview.State);
		Assert.Null(preview.Text);
	}

	[Fact]
	public async Task Text_ThatIsNotUtf8_IsRefused_RatherThanShownWithReplacements()
	{
		_files.WriteElsewhere("/srv/latin1.txt", [0x63, 0x61, 0x66, 0xE9, 0x0A]);

		EntryPreview preview = await LoadAsync(File("latin1.txt", 5));

		Assert.Equal(EntryPreviewState.NotUtf8, preview.State);
		Assert.Null(preview.Text);
	}

	[Fact]
	public async Task EmptyFile_SaysSo()
	{
		_files.WriteElsewhere("/srv/empty.txt", []);

		Assert.Equal(EntryPreviewState.Empty, (await LoadAsync(File("empty.txt", 0))).State);
	}

	[Theory]
	[InlineData("chart.png", "image/png")]
	[InlineData("photo.JPG", "image/jpeg")]
	[InlineData("anim.gif", "image/gif")]
	[InlineData("banner.webp", "image/webp")]
	public async Task Image_UnderTheCap_IsADataAddressOfItsBytes(string name, string type)
	{
		byte[] bytes = [.. Enumerable.Range(0, 5000).Select(index => (byte)index)];
		_files.WriteElsewhere("/srv/" + name, bytes);

		EntryPreview preview = await LoadAsync(File(name, bytes.Length));

		Assert.Equal(EntryPreviewState.Image, preview.State);
		Assert.Equal("data:" + type + ";base64," + Convert.ToBase64String(bytes), preview.ImageAddress);
	}

	[Fact]
	public async Task Svg_GoesToTheImageAsData_NeverAsMarkup()
	{
		const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"><script>alert(2)</script></svg>";
		_files.WriteElsewhere("/srv/badge.svg", svg);

		EntryPreview preview = await LoadAsync(File("badge.svg", svg.Length));

		Assert.Equal(EntryPreviewState.Image, preview.State);
		Assert.StartsWith("data:image/svg+xml;base64,", preview.ImageAddress, StringComparison.Ordinal);
		Assert.DoesNotContain("<", preview.ImageAddress, StringComparison.Ordinal);
		Assert.Null(preview.Text);
	}

	[Fact]
	public async Task Image_ListedPastTheCap_IsTooLarge_WithoutAnyRead()
	{
		_files.WriteElsewhere("/srv/panorama.png", new byte[16]);

		EntryPreview preview = await LoadAsync(File("panorama.png", EntryPreviews.ImageBytes + 1L));

		Assert.Equal(EntryPreviewState.TooLarge, preview.State);
		Assert.Equal(0, _files.Opened);
	}

	[Fact]
	public async Task Image_ThroughALink_StopsReadingOneBytePastTheCap()
	{
		// A link lists its own size, so the read has to find out.
		_files.WriteElsewhere("/srv/latest.png", new byte[EntryPreviews.ImageBytes + 100_000]);
		RemoteFileEntry link = new()
		{
			Name = "latest.png",
			Path = "/srv/latest.png",
			Kind = RemoteEntryKind.SymbolicLink,
			LinkTargetKind = RemoteEntryKind.File,
			Size = 9,
		};

		EntryPreview preview = await LoadAsync(link);

		Assert.Equal(EntryPreviewState.TooLarge, preview.State);
		Assert.InRange(_files.BytesRead, EntryPreviews.ImageBytes + 1, EntryPreviews.ImageBytes + 1);
	}

	[Fact]
	public async Task Image_OfExactlyTheCap_IsShown()
	{
		_files.WriteElsewhere("/srv/edge.png", new byte[EntryPreviews.ImageBytes]);

		EntryPreview preview = await LoadAsync(File("edge.png", EntryPreviews.ImageBytes));

		Assert.Equal(EntryPreviewState.Image, preview.State);
	}

	[Fact]
	public async Task ReadThatFails_SaysWhy_InsteadOfThrowing()
	{
		_files.ReadFailure = new RemoteFileSystemException(RemoteFileErrorKind.PermissionDenied, "Permission denied", "/srv/secret.txt");

		EntryPreview preview = await LoadAsync(File("secret.txt", 10));

		Assert.Equal(EntryPreviewState.Failed, preview.State);
		Assert.False(string.IsNullOrWhiteSpace(preview.Problem));
	}

	[Fact]
	public async Task Cancelled_Throws_RatherThanReportingAFailure()
	{
		_files.WriteElsewhere("/srv/notes.md", "# Notes\n");
		using CancellationTokenSource cancelled = new();
		await cancelled.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => EntryPreviews.LoadAsync(_files, File("notes.md", 8), cancelled.Token));
	}

	[Fact]
	public async Task Folder_IsNeverOpened()
	{
		EntryPreview preview = await LoadAsync(new RemoteFileEntry { Name = "src", Path = "/srv/src", Kind = RemoteEntryKind.Directory });

		Assert.Equal(EntryPreviewState.None, preview.State);
		Assert.Equal(0, _files.Opened);
	}

	[Fact]
	public void Preview_NeverPrintsTheTextItHolds()
	{
		EntryPreview preview = EntryPreview.OfText("DB_PASSWORD=hunter2", isCut: false);

		Assert.DoesNotContain("hunter2", preview.ToString(), StringComparison.Ordinal);
	}

	private static RemoteFileEntry File(string name, long size) =>
		new() { Name = name, Path = "/srv/" + name, Kind = RemoteEntryKind.File, Size = size };

	private Task<EntryPreview> LoadAsync(RemoteFileEntry entry) => EntryPreviews.LoadAsync(_files, entry, Ct).WaitAsync(Limit, Ct);
}
