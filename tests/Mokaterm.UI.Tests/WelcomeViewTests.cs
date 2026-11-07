using System.Net;
using System.Text.RegularExpressions;
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
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Connections;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The workspace's home screen: it says something different to a vault with no host in it, it keeps the offer to reopen
/// the last run's sessions, and it shows the keys in force for what it offers rather than fixed text.
/// </summary>
public sealed partial class WelcomeViewTests
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

	private static readonly Guid HostId = Guid.Parse("6f1c9a10-0d24-4f3a-9c21-7b0f36b1b001");

	private static readonly Guid FavoriteLoginId = Guid.Parse("6f1c9a10-0d24-4f3a-9c21-7b0f36b1b002");

	private static readonly Guid RecentLoginId = Guid.Parse("6f1c9a10-0d24-4f3a-9c21-7b0f36b1b003");

	/// <summary>The actions the getting-started list offers, in the order it shows them.</summary>
	private static readonly string[] StartCommands =
	[
		ShellCommandIds.NewHost,
		ShellCommandIds.ImportConnections,
		ShellCommandIds.QuickConnect,
		ShellCommandIds.Shortcuts,
	];

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Render_NoSavedHost_LeadsWithGettingStartedAndNamesTheImportSources()
	{
		string html = await RenderAsync(ConnectionCatalog.Empty);

		Assert.Contains("Nothing saved yet", html, StringComparison.Ordinal);
		Assert.Contains("Start here", html, StringComparison.Ordinal);
		Assert.Contains("Add a host", html, StringComparison.Ordinal);
		Assert.Contains("Import from another client", html, StringComparison.Ordinal);
		Assert.Contains("OpenSSH config, PuTTY or WinSCP", html, StringComparison.Ordinal);
		Assert.Contains("Connect without saving", html, StringComparison.Ordinal);

		// The quick connect box is in the top bar, so the row says what to type there, with one @ and not Razor's escape.
		Assert.Contains("Type user@host", html, StringComparison.Ordinal);

		// Nothing is saved, so there is no login to suggest and nothing to say about one.
		Assert.DoesNotContain("Ready to connect", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Favorites", html, StringComparison.Ordinal);

		// No module is loaded, so there is no protocol to name either.
		Assert.DoesNotContain("Connects over", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_NoSavedHost_NamesTheProtocolsThisCopyConnectsOver()
	{
		QuickConnectProtocols protocols = new();

		string html = await RenderAsync(ConnectionCatalog.Empty, new UiStateDocument(), protocols);

		Assert.Contains("Connects over", html, StringComparison.Ordinal);
		List<string> expected = [.. protocols.Protocols.Select(protocol => protocol.DisplayName)];
		Assert.Equal(expected, ProtocolTags(html));
	}

	// A binding the user changed has to show here, so the rows are checked against the bindings in force.
	[Fact]
	public async Task Render_NoSavedHost_ShowsTheKeysEachActionAnswersTo()
	{
		string html = await RenderAsync(ConnectionCatalog.Empty);

		List<string[]> expected = [];
		foreach (string commandId in StartCommands)
		{
			if (ShellShortcuts.KeysFor(commandId) is { Count: > 0 } keys)
			{
				expected.Add([.. keys]);
			}
		}

		Assert.NotEmpty(expected);
		Assert.Equal(expected, KeyGroups(html));
	}

	[Fact]
	public async Task Render_SavedLogins_LeadsWithFavoritesAndRecent()
	{
		string html = await RenderAsync(CatalogWith(
			Login(FavoriteLoginId, "root", favorite: true),
			Login(RecentLoginId, "deploy", lastConnected: DateTimeOffset.UtcNow.AddMinutes(-5))));

		Assert.Contains("Ready to connect", html, StringComparison.Ordinal);
		Assert.Contains("Favorites", html, StringComparison.Ordinal);
		Assert.Contains("Recent", html, StringComparison.Ordinal);
		Assert.Contains("root@10.0.0.4", html, StringComparison.Ordinal);
		Assert.Contains("deploy@10.0.0.4", html, StringComparison.Ordinal);

		// The actions stay reachable, without the explanations a first-time screen needs.
		Assert.Contains("Quick connect", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Nothing saved yet", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Start here", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Type user@host", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_SavedLogins_SaysWhatTheVaultHolds()
	{
		ConnectionCatalog oneLogin = CatalogWith(Login(RecentLoginId, "deploy"));
		ConnectionCatalog filed = new(
			[new ConnectionFolder { Id = Guid.NewGuid(), Name = "Lab" }],
			oneLogin.Hosts,
			[Login(FavoriteLoginId, "root", favorite: true), Login(RecentLoginId, "deploy")]);

		// The renderer writes the middle dot as an entity.
		Assert.Contains("1 host · 1 login<", WebUtility.HtmlDecode(await RenderAsync(oneLogin)), StringComparison.Ordinal);
		Assert.Contains("1 host · 2 logins · 1 folder<", WebUtility.HtmlDecode(await RenderAsync(filed)), StringComparison.Ordinal);
	}

	// The star sits on the row and a vault of thirty favorites would stretch the grid, so the rest are counted.
	[Fact]
	public async Task Render_MoreFavoritesThanTheLimit_CountsTheRest()
	{
		string html = await RenderAsync(
			CatalogWith(Login(FavoriteLoginId, "root", favorite: true), Login(RecentLoginId, "deploy", favorite: true)),
			new UiStateDocument(),
			new NoProtocols(),
			new GeneralSettings { RecentConnectionsLimit = 1 });

		Assert.Contains("deploy@10.0.0.4", html, StringComparison.Ordinal);
		Assert.DoesNotContain("root@10.0.0.4", html, StringComparison.Ordinal);
		Assert.Contains("1 more in the connections panel.", html, StringComparison.Ordinal);
		Assert.Matches("mt-welcome-count[^>]*>2<", html);
	}

	// A port is worth showing only when it is not the one the protocol would use anyway.
	[Fact]
	public async Task Render_ALoginOnItsOwnPort_ShowsThePort()
	{
		string html = await RenderAsync(
			CatalogWith(
				Login(FavoriteLoginId, "root", favorite: true) with { Port = 2222 },
				Login(RecentLoginId, "deploy", favorite: true) with { Port = 22 }),
			new UiStateDocument(),
			new QuickConnectProtocols());

		Assert.Equal("2222", Assert.Single(LoginPort().Matches(html)).Groups[1].Value);
	}

	[Fact]
	public async Task Render_AProductionHost_IsTaggedOnItsRows()
	{
		ConnectionCatalog catalog = new(
			[],
			[new HostProfile { Id = HostId, Name = "web-1", Address = "10.0.0.4", Environment = HostEnvironment.Production }],
			[Login(FavoriteLoginId, "root", favorite: true)]);

		string html = await RenderAsync(catalog);

		Assert.Contains("Production", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Production", await RenderAsync(CatalogWith(Login(FavoriteLoginId, "root", favorite: true))), StringComparison.Ordinal);
	}

	// Hosts saved but never used: neither list has anything, so the screen says where they are instead of looking empty.
	[Fact]
	public async Task Render_SavedHostNeverConnectedTo_PointsAtTheConnectionsPanel()
	{
		string html = await RenderAsync(CatalogWith(Login(RecentLoginId, "deploy")));

		Assert.Contains("Your hosts are in the connections panel", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Nothing saved yet", html, StringComparison.Ordinal);

		IReadOnlyList<string> panelKeys = ShellShortcuts.KeysFor(ShellCommandIds.ToggleConnections);
		Assert.NotEmpty(panelKeys);
		Assert.Contains(KeyGroups(html), group => group.SequenceEqual(panelKeys));

		// The panel is showing, so there is nothing to bring back.
		Assert.DoesNotContain("Show the connections panel", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_SavedHostNeverConnectedTo_PanelHidden_OffersToShowIt()
	{
		string html = await RenderAsync(
			CatalogWith(Login(RecentLoginId, "deploy")),
			new UiStateDocument { ConnectionsPanelCollapsed = true },
			new NoProtocols());

		Assert.Contains("Show the connections panel", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ASessionWasOpenLastTime_OffersToReopenIt()
	{
		string html = await RenderAsync(CatalogWith(Login(RecentLoginId, "deploy")), RecentLoginId);

		Assert.Contains("Where you left off", html, StringComparison.Ordinal);
		Assert.Contains("One session was open when Mokaterm last closed.", html, StringComparison.Ordinal);
		Assert.Contains("Reopen", html, StringComparison.Ordinal);
		Assert.Contains("Not now", html, StringComparison.Ordinal);

		// The login is in neither list, so the offer is the only place that names it.
		Assert.Contains("deploy@10.0.0.4", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ManySessionsWereOpenLastTime_NamesTheFirstAndCountsTheRest()
	{
		ConnectionProfile[] logins = [.. Enumerable.Range(1, 6).Select(n => Login(Guid.NewGuid(), $"user{n}"))];

		string html = await RenderAsync(CatalogWith(logins), [.. logins.Select(login => login.Id)]);

		Assert.Contains("6 sessions were open when Mokaterm last closed.", html, StringComparison.Ordinal);
		Assert.Contains("user4@10.0.0.4", html, StringComparison.Ordinal);
		Assert.DoesNotContain("user5@10.0.0.4", html, StringComparison.Ordinal);
		Assert.Contains("2 more", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ThePendingSessionsLoginIsGone_OffersNothing()
	{
		string html = await RenderAsync(ConnectionCatalog.Empty, RecentLoginId);

		Assert.DoesNotContain("Where you left off", html, StringComparison.Ordinal);
	}

	private static ConnectionCatalog CatalogWith(params ConnectionProfile[] logins) =>
		new([], [new HostProfile { Id = HostId, Name = "web-1", Address = "10.0.0.4" }], logins);

	private static ConnectionProfile Login(Guid id, string user, bool favorite = false, DateTimeOffset? lastConnected = null) => new()
	{
		Id = id,
		HostId = HostId,
		ProtocolId = "ssh",
		Username = user,
		IsFavorite = favorite,
		LastConnectedAt = lastConnected,
	};

	/// <summary>The keys each row shows, in the order the screen shows them, read back out of the rendered caps.</summary>
	private static List<string[]> KeyGroups(string html)
	{
		List<string[]> groups = [];
		foreach (Match span in KeySpan().Matches(html))
		{
			groups.Add([.. KeyCap().Matches(span.Groups[1].Value).Select(cap => cap.Groups[1].Value)]);
		}

		return groups;
	}

	private static List<string> ProtocolTags(string html)
	{
		Match strip = ProtocolStrip().Match(html);
		Assert.True(strip.Success, "The screen shows no protocol strip.");
		return [.. TagText().Matches(strip.Groups[1].Value).Select(tag => tag.Groups[1].Value)];
	}

	/// <summary>
	/// Renders the screen over <paramref name="catalog"/>, with <paramref name="restorePending"/> saved as the logins the
	/// previous run had open.
	/// </summary>
	private static Task<string> RenderAsync(ConnectionCatalog catalog, params Guid[] restorePending) =>
		RenderAsync(catalog, new UiStateDocument { RestoreConnectionIds = restorePending }, new NoProtocols());

	private static async Task<string> RenderAsync(
		ConnectionCatalog catalog,
		UiStateDocument savedState,
		IProtocolRegistry protocols,
		GeneralSettings? general = null)
	{
		MemoryAppDataStore store = new();
		store.Set("ui-state", savedState);

		await using UiStateStore uiState = new(store, TimeProvider.System, NullLogger<UiStateStore>.Instance);
		await uiState.LoadAsync(Ct).WaitAsync(Patience, Ct);

		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FixedSettings settings = general is null ? new() : new(general);
		using SessionRestore restore = new(
			manager,
			workspace,
			uiState,
			settings,
			new UserInteractionService(),
			NullLogger<SessionRestore>.Instance);

		await restore.StartAsync().WaitAsync(Patience, Ct);

		return await StaticRender.RenderAsync<WelcomeView>(
			services =>
			{
				services.AddMokaRed();
				services.AddMokatermUiCommon();
				services.AddSingleton<IVault, UnlockedVault>();
				services.AddSingleton<IConnectionRepository>(new FixedCatalog(catalog));
				services.AddSingleton<ICredentialStore, UntouchedCredentials>();
				services.AddSingleton(protocols);
				services.AddSingleton<ISettingsService>(settings);
				services.AddSingleton<ISessionManager>(manager);
				services.AddSingleton(workspace);
				services.AddSingleton(restore);
				services.AddSingleton(uiState);
				services.AddScoped<IUserInteraction, UserInteractionService>();
				services.AddScoped<ShellState>();
				services.AddScoped<CatalogState>();
				services.AddScoped<SessionActions>();
				services.AddScoped<ConnectionActions>();
			},
			new Dictionary<string, object?>(StringComparer.Ordinal)).WaitAsync(Patience, Ct);
	}

	[GeneratedRegex("<span class=\"mt-welcome-keys[^\"]*\"[^>]*>(.*?)</span>", RegexOptions.Singleline)]
	private static partial Regex KeySpan();

	[GeneratedRegex("<kbd[^>]*>([^<]*)</kbd>")]
	private static partial Regex KeyCap();

	[GeneratedRegex("<div class=\"mt-welcome-protocols\"[^>]*>(.*?)</section>", RegexOptions.Singleline)]
	private static partial Regex ProtocolStrip();

	[GeneratedRegex("<span class=\"moka-tag-text\"[^>]*>([^<]*)</span>")]
	private static partial Regex TagText();

	[GeneratedRegex("<span class=\"mt-welcome-login-port\"[^>]*>:([^<]*)</span>")]
	private static partial Regex LoginPort();

	/// <summary>The catalog the screen reads, handed over as it is.</summary>
	private sealed class FixedCatalog(ConnectionCatalog catalog) : IConnectionRepository
	{
		public event Action? Changed
		{
			add { }
			remove { }
		}

		public ValueTask<ConnectionCatalog> GetCatalogAsync(CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(catalog);

		public Task SaveFolderAsync(ConnectionFolder folder, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task DeleteFolderAsync(Guid folderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task SaveHostAsync(HostProfile host, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task DeleteHostAsync(Guid hostId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task SaveConnectionAsync(ConnectionProfile connection, CancellationToken cancellationToken = default) => throw new NotSupportedException();

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

	/// <summary>Only the login menu reaches credentials, and rendering never opens one.</summary>
	private sealed class UntouchedCredentials : ICredentialStore
	{
		public event Action? Changed
		{
			add { }
			remove { }
		}

		public ValueTask<IReadOnlyList<CredentialInfo>> ListAsync(CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public ValueTask<CredentialInfo?> FindAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public ValueTask<CredentialInfo> SaveAsync(CredentialInfo info, CredentialSecretInput? secret, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public ValueTask<CredentialSecret> RevealAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

		public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
	}

	/// <summary>No module is loaded, so a login's protocol is named after its id and drawn with the default icon.</summary>
	private sealed class NoProtocols : IProtocolRegistry
	{
		public IReadOnlyList<ProtocolDescriptor> Protocols => [];

		public IProtocolProvider? Find(string protocolId) => null;

		public IProtocolProvider Get(string protocolId) => throw new KeyNotFoundException(protocolId);
	}
}
