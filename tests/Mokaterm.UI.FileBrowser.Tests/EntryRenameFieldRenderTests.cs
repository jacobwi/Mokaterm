using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.FileSystem;
using Mokaterm.UI.FileBrowser.Browsing;

namespace Mokaterm.UI.FileBrowser.Tests;

public sealed class EntryRenameFieldRenderTests
{
	private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData("report.txt", RemoteEntryKind.File, 6)]
	[InlineData("site.tar.gz", RemoteEntryKind.File, 4)]
	[InlineData(".bashrc", RemoteEntryKind.File, 7)]
	[InlineData("conf.d", RemoteEntryKind.Directory, 6)]
	public async Task TheField_OpensOnTheNameAndMarksWhatIsSelected(string name, RemoteEntryKind kind, int selectedTo)
	{
		string html = await RenderAsync(new RemoteFileEntry { Path = "/srv/" + name, Name = name, Kind = kind });

		Assert.Contains($"value=\"{name}\"", html, StringComparison.Ordinal);
		Assert.Contains($"data-name-end=\"{selectedTo}\"", html, StringComparison.Ordinal);
		Assert.Contains(kind == RemoteEntryKind.Directory ? "aria-label=\"Folder name\"" : "aria-label=\"File name\"", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(RemoteFileEntry entry) =>
		StaticRender.RenderAsync<EntryRenameField>(
			services => services.AddMokaRed(),
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				[nameof(EntryRenameField.Entry)] = entry,
				[nameof(EntryRenameField.Save)] = (Func<string, Task>)(_ => Task.CompletedTask),
			})
			.WaitAsync(Limit, Ct);
}
