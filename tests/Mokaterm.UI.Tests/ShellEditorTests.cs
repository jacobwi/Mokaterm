using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Storage;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Connections;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The shell's editors all follow <see cref="ShellEditorBase{TRequest}"/>: they show the open editor request when it is
/// theirs, load what it names, and stay out of the way otherwise.
/// </summary>
public sealed class ShellEditorTests
{
	private static readonly Guid FolderId = Guid.Parse("8f1f0d24-0d24-4f3a-9c21-7b0f36b1a001");

	[Fact]
	public async Task Render_NoOpenEditor_ShowsNothing()
	{
		string html = await RenderFolderEditorAsync(shell => { });

		Assert.Equal(string.Empty, html);
	}

	// Every editor is mounted all the time, so each one has to ignore the requests that belong to another.
	[Fact]
	public async Task Render_AnotherEditorsRequest_ShowsNothing()
	{
		string html = await RenderFolderEditorAsync(shell => shell.OpenEditor(new HostEditorRequest(null)));

		Assert.Equal(string.Empty, html);
	}

	[Fact]
	public async Task Render_ARequestWithoutAFolder_OpensOnANewOne()
	{
		string html = await RenderFolderEditorAsync(shell => shell.OpenEditor(new FolderEditorRequest(null)));

		Assert.Contains("New folder", html, StringComparison.Ordinal);
		Assert.Contains("Create", html, StringComparison.Ordinal);
		Assert.Contains("Created at the top level.", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ARequestNamingAFolder_LoadsItFromTheCatalog()
	{
		string html = await RenderFolderEditorAsync(shell => shell.OpenEditor(new FolderEditorRequest(FolderId)));

		Assert.Contains("Rename folder", html, StringComparison.Ordinal);
		Assert.Contains("Production", html, StringComparison.Ordinal);
		Assert.DoesNotContain("no longer exists", html, StringComparison.Ordinal);
	}

	// A locked vault is reported as itself. Wrapping its message would read as "Could not open this login: The vault is
	// locked", which says nothing about unlocking it.
	[Fact]
	public async Task Render_ALockedVault_SaysToUnlockItRatherThanWrappingItsMessage()
	{
		ShellState shell = NewShell();
		shell.OpenEditor(new ConnectionEditorRequest());

		string html = await StaticRender.RenderAsync<ConnectionEditor>(
			services =>
			{
				Configure(services, shell);
				services.AddMokatermUiCommon();
				services.AddSingleton<ICredentialStore, LockedCredentials>();
				services.AddSingleton<IProtocolRegistry, NoProtocols>();
				services.AddSingleton<ITerminalThemeCatalog, NoThemes>();
				services.AddSingleton<ISettingsService>(new FixedSettings());
				services.AddSingleton<ISessionManager, ManualSessionManager>();
				services.AddScoped<IUserInteraction, UserInteractionService>();
				services.AddScoped<SessionWorkspace>();
				services.AddScoped<SessionRestore>();
				services.AddScoped<SessionActions>();
			},
			new Dictionary<string, object?>(StringComparer.Ordinal));

		Assert.Contains("Unlock the vault to open this login.", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Could not open this login", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderFolderEditorAsync(Action<ShellState> open)
	{
		ShellState shell = NewShell();
		open(shell);
		return StaticRender.RenderAsync<FolderEditor>(
			services => Configure(services, shell),
			new Dictionary<string, object?>(StringComparer.Ordinal));
	}

	private static ShellState NewShell() =>
		new(new UiStateStore(new MemoryAppDataStore(), TimeProvider.System, NullLogger<UiStateStore>.Instance));

	private static void Configure(IServiceCollection services, ShellState shell)
	{
		services.AddMokaRed();
		services.AddSingleton(TimeProvider.System);
		services.AddSingleton<IAppDataStore, MemoryAppDataStore>();
		services.AddSingleton<IVault, UnlockedVault>();
		services.AddSingleton<IConnectionRepository, OneFolderRepository>();
		services.AddSingleton(shell);
		services.AddSingleton(new UiStateStore(new MemoryAppDataStore(), TimeProvider.System, NullLogger<UiStateStore>.Instance));
		services.AddScoped<CatalogState>();
		services.AddScoped<FormInterop>();
	}

	/// <summary>A catalog with one folder to rename, and nothing else.</summary>
	private sealed class OneFolderRepository : IConnectionRepository
	{
		private static readonly ConnectionCatalog Catalog = new(
			[new ConnectionFolder { Id = FolderId, Name = "Production" }],
			[],
			[]);

		public event Action? Changed
		{
			add { }
			remove { }
		}

		public ValueTask<ConnectionCatalog> GetCatalogAsync(CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(Catalog);

		public Task SaveFolderAsync(ConnectionFolder folder, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task DeleteFolderAsync(Guid folderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task SaveHostAsync(HostProfile host, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task DeleteHostAsync(Guid hostId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task SaveConnectionAsync(ConnectionProfile connection, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task DeleteConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task MarkConnectedAsync(Guid connectionId, DateTimeOffset connectedAt, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();
	}

	/// <summary>Unlocked, so the catalog loads; nothing else is asked of it.</summary>
	private sealed class UnlockedVault : IVault
	{
		public event Action<VaultStatus>? StatusChanged
		{
			add { }
			remove { }
		}

		public VaultStatus Status => VaultStatus.Unlocked;

		public LockReason? LastLockReason => null;

		public bool IsDeviceUnlockAvailable => false;

		public bool IsDeviceUnlockEnabled => false;

		public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task CreateAsync(string masterPassword, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<UnlockResult> UnlockAsync(string masterPassword, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<UnlockResult> UnlockWithDeviceAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task SetDeviceUnlockAsync(bool enabled, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task<UnlockResult> ChangeMasterPasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public void Lock(LockReason reason = LockReason.User)
		{
		}

		public void ReportActivity()
		{
		}

		public Task ResetAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
	}

	/// <summary>A credential store on a vault that locked between opening the editor and reading it.</summary>
	private sealed class LockedCredentials : ICredentialStore
	{
		public event Action? Changed
		{
			add { }
			remove { }
		}

		public ValueTask<IReadOnlyList<CredentialInfo>> ListAsync(CancellationToken cancellationToken = default) =>
			throw new VaultLockedException();

		public ValueTask<CredentialInfo?> FindAsync(Guid id, CancellationToken cancellationToken = default) => throw new VaultLockedException();

		public ValueTask<CredentialInfo> SaveAsync(CredentialInfo info, CredentialSecretInput? secret, CancellationToken cancellationToken = default) =>
			throw new VaultLockedException();

		public ValueTask<CredentialSecret> RevealAsync(Guid id, CancellationToken cancellationToken = default) => throw new VaultLockedException();

		public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new VaultLockedException();
	}

	/// <summary>The editor asks the catalog nothing once its load failed, so nothing here has to answer.</summary>
	private sealed class NoThemes : ITerminalThemeCatalog
	{
		public TerminalTheme Default => throw new NotSupportedException();

		public IReadOnlyList<TerminalTheme> Themes => throw new NotSupportedException();

		public TerminalTheme Get(string? id) => throw new NotSupportedException();
	}
}
