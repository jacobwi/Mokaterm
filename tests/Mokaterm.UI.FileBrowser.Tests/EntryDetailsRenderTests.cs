using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.FileBrowser.Details;
using Mokaterm.UI.FileBrowser.Extensions;
using Mokaterm.UI.FileBrowser.Tests.Fakes;
using static Mokaterm.UI.FileBrowser.Tests.Fakes.FakeFileSystem;

namespace Mokaterm.UI.FileBrowser.Tests;

/// <summary>
/// The details pane rendered for real: the listing's facts for one entry or several, and the preview it reads once the
/// selection settles, which a clock that is always due lets a static render wait for.
/// </summary>
public sealed class EntryDetailsRenderTests
{
	private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task OneFile_ShowsItsFacts_AndOwnerAndModeOnlyWhereTheFileSystemHasThem()
	{
		RemoteFileEntry file = File("/srv/app/report.csv", size: 1536, mode: Mode("640"), owner: "deploy", group: "www-data") with
		{
			LastModified = new DateTimeOffset(2026, 9, 17, 14, 5, 0, TimeSpan.Zero),
		};
		TextFileSystem rich = new TextFileSystem { Features = RemoteFileSystemFeatures.Permissions | RemoteFileSystemFeatures.Ownership }
			.WriteElsewhere(file.Path, "a,b\n");

		string html = await RenderAsync([file], rich);
		string plain = await RenderAsync([file], new TextFileSystem().WriteElsewhere(file.Path, "a,b\n"));

		Assert.Contains("report.csv", html, StringComparison.Ordinal);
		Assert.Contains(">Kind<", html, StringComparison.Ordinal);
		Assert.Contains(">File<", html, StringComparison.Ordinal);
		Assert.Contains("1.5 KB (1,536 bytes)", html, StringComparison.Ordinal);
		Assert.Contains(DisplayTime(file.LastModified), html, StringComparison.Ordinal);
		Assert.Contains("-rw-r----- (640)", html, StringComparison.Ordinal);
		Assert.Contains("deploy:www-data", html, StringComparison.Ordinal);
		Assert.Contains("/srv/app/report.csv", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Copy path\"", html, StringComparison.Ordinal);

		Assert.DoesNotContain(">Permissions<", plain, StringComparison.Ordinal);
		Assert.DoesNotContain(">Owner<", plain, StringComparison.Ordinal);
		Assert.Contains("1.5 KB (1,536 bytes)", plain, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Link_SaysWhereItLeads()
	{
		RemoteFileEntry link = Link("/srv/current", RemoteEntryKind.Directory) with { LinkTarget = "/srv/releases/42" };

		string html = await RenderAsync([link], new TextFileSystem());

		Assert.Contains(">Link target<", html, StringComparison.Ordinal);
		Assert.Contains("/srv/releases/42 (folder)", html, StringComparison.Ordinal);
		Assert.Contains("Link to a folder", html, StringComparison.Ordinal);
		Assert.DoesNotContain(">Preview<", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task SeveralEntries_CountThem_AndAddUpTheFilesOnly()
	{
		RemoteFileEntry[] entries =
		[
			File("/srv/a.bin", size: 1024),
			File("/srv/b.bin", size: 2048),
			Folder("/srv/cache"),
		];

		string html = await RenderAsync(entries, new TextFileSystem());

		Assert.Contains("3 items selected", html, StringComparison.Ordinal);
		Assert.Contains("1 folder, 2 files", html, StringComparison.Ordinal);
		Assert.Contains("3 KB (3,072 bytes)", html, StringComparison.Ordinal);
		Assert.Contains("folders and links are left out", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task NothingSelected_DescribesTheFolderOnScreen()
	{
		string html = await RenderAsync([], new TextFileSystem(), folder: "/var/log", summary: "42 items, 3 not shown");

		Assert.Contains(">log<", html, StringComparison.Ordinal);
		Assert.Contains("42 items, 3 not shown", html, StringComparison.Ordinal);
		Assert.Contains("/var/log", html, StringComparison.Ordinal);
		Assert.Contains("Select an item", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TextFile_ShowsItsStart_EscapedAndReadOnly()
	{
		TextFileSystem files = new TextFileSystem().WriteElsewhere("/srv/index.html", "<script>alert(1)</script>\n<p>hello</p>\n");

		string html = await RenderAsync([File("/srv/index.html", size: 40)], files);

		Assert.Contains("<pre class=\"fb-details-text\"", html, StringComparison.Ordinal);
		Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
		Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
		Assert.DoesNotContain("<textarea", html, StringComparison.Ordinal);
		Assert.Equal(1, files.Opened);
	}

	[Fact]
	public async Task LongTextFile_SaysOnlyTheStartIsShown()
	{
		TextFileSystem files = new TextFileSystem().WriteElsewhere("/srv/big.log", new string('x', EntryPreviews.TextBytes * 3));

		string html = await RenderAsync([File("/srv/big.log", size: EntryPreviews.TextBytes * 3)], files);

		Assert.Contains("The first 32 KB of the file.", html, StringComparison.Ordinal);
		Assert.InRange(files.BytesRead, 1, EntryPreviews.TextBytes + 1);
	}

	[Fact]
	public async Task Svg_IsAnImageOfData_NeverMarkupInThePage()
	{
		const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"><script>alert(2)</script><rect width=\"4\" height=\"4\"/></svg>";
		TextFileSystem files = new TextFileSystem().WriteElsewhere("/srv/badge.svg", svg);

		string html = await RenderAsync([File("/srv/badge.svg", size: svg.Length)], files);

		Assert.Contains("<img class=\"fb-details-image\" src=\"data:image/svg+xml;base64,", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
		Assert.DoesNotContain("<rect width=", html, StringComparison.Ordinal);
		Assert.DoesNotContain("onload", html, StringComparison.Ordinal);
		Assert.DoesNotContain("alert(", html, StringComparison.Ordinal);
		Assert.Contains("draggable=\"false\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task LargeImage_SaysSo_WithoutReadingIt()
	{
		TextFileSystem files = new TextFileSystem().WriteElsewhere("/srv/panorama.png", new byte[8]);

		string html = await RenderAsync([File("/srv/panorama.png", size: EntryPreviews.ImageBytes * 2L)], files);

		Assert.Contains("Too large to preview: images are shown up to 2 MB.", html, StringComparison.Ordinal);
		Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
		Assert.Equal(0, files.Opened);
	}

	[Fact]
	public async Task BinaryFile_HasNoPreview()
	{
		TextFileSystem files = new TextFileSystem().WriteElsewhere("/srv/app", [0x7F, 0x45, 0x4C, 0x46, 0x00]);

		string html = await RenderAsync([File("/srv/app", size: 5)], files);

		Assert.Contains("Not text, so there is no preview.", html, StringComparison.Ordinal);
		Assert.DoesNotContain("<pre", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task FailedRead_IsSaidInThePane()
	{
		TextFileSystem files = new() { ReadFailure = new RemoteFileSystemException(RemoteFileErrorKind.PermissionDenied, "Permission denied", "/root/.env") };

		string html = await RenderAsync([File("/root/.env", size: 64)], files);

		Assert.Contains("Could not read it.", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task HiddenBrowser_ReadsNothing()
	{
		TextFileSystem files = new TextFileSystem().WriteElsewhere("/srv/notes.md", "# Notes\n");

		string html = await RenderAsync([File("/srv/notes.md", size: 8)], files, active: false);

		Assert.Contains(">Preview<", html, StringComparison.Ordinal);
		Assert.DoesNotContain("<pre", html, StringComparison.Ordinal);
		Assert.Equal(0, files.Opened);
	}

	[Fact]
	public async Task Folder_IsNeverRead()
	{
		TextFileSystem files = new();

		string html = await RenderAsync([Folder("/srv/www")], files);

		Assert.Contains(">Folder<", html, StringComparison.Ordinal);
		Assert.DoesNotContain(">Preview<", html, StringComparison.Ordinal);
		Assert.Equal(0, files.Opened);
	}

	private static string DisplayTime(DateTimeOffset? value) => Common.Formatting.DisplayFormat.Timestamp(value);

	private static Task<string> RenderAsync(
		RemoteFileEntry[] entries,
		IRemoteFileSystem fileSystem,
		bool active = true,
		string folder = "/srv",
		string? summary = null) =>
		StaticRender.RenderAsync<EntryDetails>(
			services =>
			{
				services.AddSingleton<TimeProvider>(new ImmediateTime());
				services.AddSingleton<ISettingsService, FakeSettingsService>();
				services.AddScoped<IUserInteraction, DismissingInteraction>();
				services.AddMokatermUiCommon();
				services.AddMokatermFileBrowser();
			},
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				[nameof(EntryDetails.Entries)] = entries,
				[nameof(EntryDetails.FileSystem)] = fileSystem,
				[nameof(EntryDetails.Active)] = active,
				[nameof(EntryDetails.Folder)] = folder,
				[nameof(EntryDetails.FolderSummary)] = summary,
			})
			.WaitAsync(Limit, Ct);
}
