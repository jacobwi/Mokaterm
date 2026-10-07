using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Storage;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Connections;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

/// <summary>Renaming a folder says when a folder beside it already reads the same, the way the host editor does.</summary>
public sealed class FolderEditorTests
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

	private static readonly Guid FolderId = Guid.Parse("9c4a1b73-0d24-4f3a-9c21-7b0f36b1e001");

	private static readonly Guid SiblingId = Guid.Parse("9c4a1b73-0d24-4f3a-9c21-7b0f36b1e002");

	private static readonly Guid NestedId = Guid.Parse("9c4a1b73-0d24-4f3a-9c21-7b0f36b1e003");

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Render_ANameAFolderBesideItHas_SaysSo()
	{
		string html = await RenderAsync(Folder(FolderId, "Clients"), Folder(SiblingId, "clients"));

		Assert.Contains("A folder here is already called Clients.", html, StringComparison.Ordinal);
	}

	// The name is only taken among siblings: the same name under another parent is a different path.
	[Fact]
	public async Task Render_ANameOnlyAFolderElsewhereHas_SaysNothing()
	{
		string html = await RenderAsync(Folder(FolderId, "Clients"), Folder(NestedId, "Clients", SiblingId), Folder(SiblingId, "Acme"));

		Assert.DoesNotContain("already called", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_TheOnlyFolderWithItsName_SaysNothing()
	{
		string html = await RenderAsync(Folder(FolderId, "Clients"));

		Assert.Contains("Rename folder", html, StringComparison.Ordinal);
		Assert.DoesNotContain("already called", html, StringComparison.Ordinal);
	}

	private static ConnectionFolder Folder(Guid id, string name, Guid? parentId = null) => new()
	{
		Id = id,
		Name = name,
		ParentId = parentId,
	};

	/// <summary>Opens the first folder for renaming, over a catalog holding all of them.</summary>
	private static Task<string> RenderAsync(params ConnectionFolder[] folders)
	{
		ShellState shell = new(new UiStateStore(new MemoryAppDataStore(), TimeProvider.System, NullLogger<UiStateStore>.Instance));
		shell.OpenEditor(new FolderEditorRequest(folders[0].Id));

		return StaticRender.RenderAsync<FolderEditor>(
			services =>
			{
				services.AddMokaRed();
				services.AddMokatermUiCommon();
				services.AddSingleton<IAppDataStore, MemoryAppDataStore>();
				services.AddSingleton<IVault, UnlockedVault>();
				services.AddSingleton<IConnectionRepository>(new EditableCatalog(new ConnectionCatalog(folders, [], [])));
				services.AddSingleton(shell);
				services.AddScoped<CatalogState>();
			},
			new Dictionary<string, object?>(StringComparer.Ordinal)).WaitAsync(Patience, Ct);
	}
}
