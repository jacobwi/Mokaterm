using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Import;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Storage;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Connections;
using Mokaterm.UI.Import;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

/// <summary>The wizard's first step says plainly what crosses: never a password, and on the desktop the keys a login names.</summary>
public sealed class ImportWizardTests
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Render_OnTheDesktop_SaysKeysGoIntoTheEncryptedVault()
	{
		string html = await RenderAsync(canImportKeys: true);

		Assert.Contains("Passwords stay where they are", html, StringComparison.Ordinal);
		Assert.Contains("copied into the keychain", html, StringComparison.Ordinal);
		Assert.Contains("encrypted vault", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_OnTheWebHost_PromisesNothingAboutKeys()
	{
		string html = await RenderAsync(canImportKeys: false);

		Assert.Contains("Passwords stay where they are", html, StringComparison.Ordinal);
		Assert.DoesNotContain("keychain", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(bool canImportKeys)
	{
		ShellState shell = new(new UiStateStore(new MemoryAppDataStore(), TimeProvider.System, NullLogger<UiStateStore>.Instance));
		shell.OpenEditor(new ImportEditorRequest());

		return StaticRender.RenderAsync<ImportWizard>(
			services =>
			{
				services.AddMokaRed();
				services.AddMokatermUiCommon();
				services.AddSingleton<IAppDataStore, MemoryAppDataStore>();
				services.AddSingleton<IVault, UnlockedVault>();
				services.AddSingleton<IConnectionRepository>(new EditableCatalog(new ConnectionCatalog([], [], [])));
				services.AddSingleton<IConnectionImporter>(new NothingToImport(canImportKeys));
				services.AddSingleton<IProtocolRegistry, NoProtocols>();
				services.AddSingleton<IUserInteraction, RecordingInteraction>();
				services.AddSingleton(shell);
				services.AddScoped<CatalogState>();
			},
			new Dictionary<string, object?>(StringComparer.Ordinal)).WaitAsync(Patience, Ct);
	}

	private sealed class NothingToImport(bool canImportKeys) : IConnectionImporter
	{
		public IReadOnlyList<ImportSourceInfo> Sources { get; } = [new() { Id = "openssh-config", DisplayName = "OpenSSH" }];

		public bool CanImportKeys => canImportKeys;

		public ValueTask<ImportPreview> PreviewAsync(string sourceId, ImportReadRequest request, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(ImportPreview.Nothing(sourceId, "No ssh config file was found."));

		public ValueTask<ImportResult> ImportAsync(ImportRequest request, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(new ImportResult());
	}
}
