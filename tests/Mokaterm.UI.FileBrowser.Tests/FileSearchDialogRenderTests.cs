using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.FileBrowser.Dialogs;
using Mokaterm.UI.FileBrowser.Extensions;

namespace Mokaterm.UI.FileBrowser.Tests;

/// <summary>Renders the search dialog as it opens. A static render presses no button, so no search runs here.</summary>
public sealed class FileSearchDialogRenderTests
{
	private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

	[Fact]
	public async Task OpensOnTheFolderWithTheLastSearch()
	{
		string html = await RenderAsync(new RemoteSearchQuery { Pattern = "*.log", MatchCase = true });

		Assert.Contains("value=\"/srv/site\"", html, StringComparison.Ordinal);
		Assert.Contains("value=\"*.log\"", html, StringComparison.Ordinal);
		Assert.Contains("Match case", html, StringComparison.Ordinal);
		Assert.Contains("Include hidden", html, StringComparison.Ordinal);
		Assert.Contains("Nothing searched yet", html, StringComparison.Ordinal);
		Assert.Contains("A name with * or ? has to match whole", html, StringComparison.Ordinal);

		// The name starts focused and Enter there finds; Enter in the results is theirs.
		Assert.Contains("data-dialog-focus", html, StringComparison.Ordinal);
		Assert.Contains("data-dialog-default", html, StringComparison.Ordinal);
		Assert.Contains("data-no-submit", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Find\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task WithoutAPattern_FindIsOff()
	{
		string html = await RenderAsync(new RemoteSearchQuery { Pattern = "" });

		Assert.Contains("disabled data-dialog-default", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(RemoteSearchQuery initialQuery)
	{
		Func<string, CancellationToken, Task<string>> resolveFolder = (text, _) => Task.FromResult(text);
		Func<string, RemoteSearchQuery, IProgress<RemoteSearchProgress>, CancellationToken, IAsyncEnumerable<RemoteFileEntry>> search =
			(_, _, _, _) => throw new InvalidOperationException("A static render runs no search.");
		Dictionary<string, object?> parameters = new(StringComparer.Ordinal)
		{
			[nameof(FileSearchDialog.Folder)] = "/srv/site",
			[nameof(FileSearchDialog.InitialQuery)] = initialQuery,
			[nameof(FileSearchDialog.ResolveFolder)] = resolveFolder,
			[nameof(FileSearchDialog.Search)] = search,
		};

		return StaticRender.RenderAsync<FileSearchDialog>(
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
