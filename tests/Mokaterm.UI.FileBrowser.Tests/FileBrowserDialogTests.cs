using Microsoft.Extensions.DependencyInjection;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Storage;
using Mokaterm.Abstractions.Transfers;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Common.Files;
using Mokaterm.UI.FileBrowser.Dialogs;
using Mokaterm.UI.FileBrowser.Extensions;
using Mokaterm.UI.FileBrowser.Tests.Fakes;

namespace Mokaterm.UI.FileBrowser.Tests;

/// <summary>
/// The file browser a module opens over its own files: the page's dialog service shows the one browser there is, sized
/// and kept the way a browser needs, and the browser renders for such files without a session behind them.
/// </summary>
public sealed class FileBrowserDialogTests
{
	private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

	private readonly ScriptedDialogs _dialogs = new();

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Show_OpensTheBrowserOverTheRequestsFiles_InThePagesDialog()
	{
		FileBrowserRequest request = Request();
		using CancellationTokenSource page = new();

		await new FileBrowserDialogService(_dialogs).ShowAsync(request, page.Token).WaitAsync(Limit, Ct);

		ScriptedDialogs.ShownDialog shown = Assert.Single(_dialogs.Shown);
		Assert.Equal(typeof(FileBrowserDialog), shown.Component);
		Assert.Equal("web-1 on build-01", shown.Title);
		Assert.Same(request, shown.Parameter<FileBrowserRequest>(nameof(FileBrowserDialog.Request)));
		Assert.Equal(page.Token, shown.Parameter<CancellationToken>(nameof(FileBrowserDialog.Closing)));

		// Escape belongs to the browser's filter and path box, and the browser scrolls its own list.
		Assert.False(shown.Options.CloseOnEscape);
		Assert.False(shown.Options.CloseOnBackdropClick);
		Assert.False(shown.Options.BodyPadding);
		Assert.False(shown.Options.ScrollBody);
		Assert.NotNull(shown.Options.Height);
	}

	[Fact]
	public async Task Show_ForAPageAlreadyGone_OpensNothing()
	{
		using CancellationTokenSource page = new();
		await page.CancelAsync();

		await new FileBrowserDialogService(_dialogs).ShowAsync(Request(), page.Token).WaitAsync(Limit, Ct);

		Assert.Empty(_dialogs.Shown);
	}

	[Fact]
	public void TheFileBrowser_RegistersTheDialogPerPage()
	{
		ServiceCollection services = new();
		services.AddMokatermFileBrowser();

		ServiceDescriptor dialog = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IFileBrowserDialog));
		Assert.Equal(ServiceLifetime.Scoped, dialog.Lifetime);
		Assert.Equal(typeof(FileBrowserDialogService), dialog.ImplementationType);
	}

	// A static render never opens the files, so this is the browser before its first listing: what matters is that it
	// renders at all without a session, and offers nothing that hangs on a saved login.
	[Fact]
	public async Task TheDialog_RendersTheBrowserWithoutASession()
	{
		string html = await StaticRender.RenderAsync<FileBrowserDialog>(
			services =>
			{
				services.AddMokaRed();
				services.AddSingleton<ISettingsService, FakeSettingsService>();
				services.AddScoped<IUserInteraction, DismissingInteraction>();
				services.AddSingleton<ILocalFileAccess, NoLocalFiles>();
				services.AddSingleton<ITransferQueue, FakeQueue>();
				services.AddSingleton<IVaultDataStore, MemoryVaultDataStore>();
				services.AddSingleton<IConnectionRepository>(new FixedConnections());
				services.AddMokatermUiCommon();
				services.AddMokatermFileBrowser();
			},
			new Dictionary<string, object?>(StringComparer.Ordinal) { [nameof(FileBrowserDialog.Request)] = Request() })
			.WaitAsync(Limit, Ct);

		Assert.Contains("mt-files-dialog", html, StringComparison.Ordinal);
		Assert.Contains("Opening files...", html, StringComparison.Ordinal);
		Assert.DoesNotContain("aria-label=\"Bookmarks\"", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Follow the terminal", html, StringComparison.Ordinal);
	}

	private static FileBrowserRequest Request() => new()
	{
		FileSystem = new FakeFileFeature(new FakeFileSystem()) { SupportsElevation = false },
		Title = "web-1 on build-01",
		Location = "web-1",
	};
}
