using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.FileBrowser.Compare;
using Mokaterm.UI.FileBrowser.Dialogs;
using Mokaterm.UI.FileBrowser.Extensions;
using Mokaterm.UI.FileBrowser.Tests.Fakes;

namespace Mokaterm.UI.FileBrowser.Tests;

/// <summary>
/// Renders the compare dialog for real: it reads the fake server through the shared walk as it opens, so what is asserted
/// here is the whole way from the walk to the ticked rows.
/// </summary>
public sealed class FolderCompareDialogRenderTests
{
	private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

	private static readonly DateTimeOffset Noon = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

	private static readonly LocalFileItem[] Picked =
	[
		new() { Name = "site", RelativePath = "site", IsDirectory = true },
		new() { Name = "css", RelativePath = "site/css", IsDirectory = true },
		Local("site/index.html", 5, Noon),
		Local("site/css/app.css", 7, Noon.AddMinutes(10)),
		Local("site/new.txt", 3, Noon),
	];

	[Fact]
	public async Task Differences_AreListedAndWhatToUploadIsTicked()
	{
		string html = await RenderAsync(Server());

		// new.txt is missing on the server and app.css is newer here; index.html is the same and filtered out.
		Assert.Contains("Upload 2 files", html, StringComparison.Ordinal);
		Assert.Contains(">css/app.css<", html, StringComparison.Ordinal);
		Assert.Contains(">new.txt<", html, StringComparison.Ordinal);
		Assert.Contains(">old.txt<", html, StringComparison.Ordinal);
		Assert.DoesNotContain(">index.html<", html, StringComparison.Ordinal);

		Assert.Contains("1 only local", html, StringComparison.Ordinal);
		Assert.Contains("1 local newer", html, StringComparison.Ordinal);
		Assert.Contains("1 only on server", html, StringComparison.Ordinal);
		Assert.Contains("1 same", html, StringComparison.Ordinal);
		Assert.Contains("nothing there is deleted", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Upload css/app.css\"", html, StringComparison.Ordinal);
		Assert.DoesNotContain("The server side is incomplete", html, StringComparison.Ordinal);
		Assert.Contains("data-no-submit", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task UnreadableServerFolder_WarnsAndTicksNothingItCouldNotSee()
	{
		FakeFileSystem server = Server();
		server.Denied.Add("/srv/site/css");

		string html = await RenderAsync(server);

		Assert.Contains("The server side is incomplete", html, StringComparison.Ordinal);
		Assert.Contains("1 folder could not be read.", html, StringComparison.Ordinal);
		Assert.Contains("2 only local", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Upload 1 file", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Upload 2 files", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task MissingServerFolder_SaysSo()
	{
		string html = await RenderAsync(new FakeFileSystem());

		Assert.Contains("Could not read the server folder", html, StringComparison.Ordinal);
		Assert.Contains("No such file or folder.", html, StringComparison.Ordinal);
		Assert.Contains("Try again", html, StringComparison.Ordinal);
	}

	private static FakeFileSystem Server() => new FakeFileSystem()
		.Contains(
			"/srv/site",
			FakeFileSystem.File("/srv/site/index.html", size: 5) with { LastModified = Noon },
			FakeFileSystem.File("/srv/site/old.txt", size: 1) with { LastModified = Noon },
			FakeFileSystem.Folder("/srv/site/css"))
		.Contains("/srv/site/css", FakeFileSystem.File("/srv/site/css/app.css", size: 7) with { LastModified = Noon });

	private static LocalFileItem Local(string relativePath, long length, DateTimeOffset modified) => new()
	{
		Name = relativePath[(relativePath.LastIndexOf('/') + 1)..],
		RelativePath = relativePath,
		Length = length,
		LastModified = modified,
		OpenReadAsync = _ => ValueTask.FromResult<Stream>(new MemoryStream([1, 2, 3])),
	};

	private static Task<string> RenderAsync(IRemoteFileSystem server)
	{
		Func<IProgress<RemoteSearchProgress>, CancellationToken, IAsyncEnumerable<RemoteFileEntry>> readRemote =
			(progress, cancellationToken) => RemoteTreeWalker.SearchAsync(server, "/srv/site", FolderComparison.RemoteQuery, progress, cancellationToken);
		Dictionary<string, object?> parameters = new(StringComparer.Ordinal)
		{
			[nameof(FolderCompareDialog.LocalItems)] = Picked,
			[nameof(FolderCompareDialog.LocalName)] = "site",
			[nameof(FolderCompareDialog.RemoteFolder)] = "/srv/site",
			[nameof(FolderCompareDialog.Destination)] = "abc@build-01:/srv/site",
			[nameof(FolderCompareDialog.ReadRemote)] = readRemote,
		};

		return StaticRender.RenderAsync<FolderCompareDialog>(
			services =>
			{
				services.AddSingleton<ISettingsService, FakeSettingsService>();
				services.AddScoped<IUserInteraction, DismissingInteraction>();
				services.AddMokatermUiCommon();
				services.AddMokatermFileBrowser();
			},
			parameters).WaitAsync(Limit, TestContext.Current.CancellationToken);
	}
}
