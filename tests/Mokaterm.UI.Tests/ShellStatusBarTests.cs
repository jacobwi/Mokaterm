using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Transfers;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Connections;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The bottom bar: it says something worth reading with nothing open, it names the active session's machine in that
/// machine's own colour, and it never repeats the connection state that the tab dot already carries.
/// </summary>
public sealed partial class ShellStatusBarTests
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

	private static readonly Guid HostId = Guid.Parse("2a7c4d10-51f8-4e2b-9d33-5c0a1e7f0001");

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Render_NothingOpen_SaysWhatTheVaultHolds()
	{
		string html = await RenderAsync(CatalogWith(3));

		Assert.Contains(">3</span><span class=\"mt-status-label\"", html, StringComparison.Ordinal);
		Assert.Contains("HOSTS", html, StringComparison.Ordinal);
		Assert.Contains("3 hosts and 3 logins saved.", html, StringComparison.Ordinal);
		Assert.Contains("NO SESSIONS", html, StringComparison.Ordinal);
		Assert.Contains("Nothing is open.", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_EmptyVault_SaysNothingIsSavedYet()
	{
		string html = await RenderAsync(ConnectionCatalog.Empty);

		Assert.Contains("NO HOSTS SAVED", html, StringComparison.Ordinal);
		Assert.Contains("Nothing is saved in this vault yet.", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ActiveSession_NamesTheProtocolTheMachineAndTheEndpoint()
	{
		string html = await RenderAsync(CatalogWith(1), (manager, _, _) => manager.Add());

		Assert.Contains("SSH", html, StringComparison.Ordinal);
		Assert.Contains("build-01.example.com", html, StringComparison.Ordinal);
		Assert.Contains(":22", html, StringComparison.Ordinal);
		Assert.Contains("SSH to bc@build-01.example.com:22", html, StringComparison.Ordinal);

		// The machine's own colour, the one its tab and session toolbar carry.
		Assert.Contains("class=\"mt-status-accent\" style=\"background:var(--mt-host-", html, StringComparison.Ordinal);
	}

	// "Session state shows once": the tab dot and the session toolbar own it, so nothing here may repeat it.
	[Fact]
	public async Task Render_ActiveSession_SaysNothingAboutItsConnectionState()
	{
		string html = await RenderAsync(CatalogWith(1), (manager, _, _) => manager.Add());

		Assert.DoesNotContain("CONNECTED", html, StringComparison.Ordinal);
		Assert.DoesNotContain("CONNECTING", html, StringComparison.Ordinal);
		Assert.DoesNotContain("DISCONNECTED", html, StringComparison.Ordinal);
		Assert.DoesNotContain("moka-status-dot", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ProductionHost_TagsTheEnvironment()
	{
		string html = await RenderAsync(
			CatalogWith(1),
			(manager, _, _) => manager.Add(new FakeSession(environment: HostEnvironment.Production)));

		Assert.Contains("PRODUCTION", html, StringComparison.Ordinal);
		Assert.Contains("Host environment: Production", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_RemoteTitle_ShowsWhatTheSessionsShellCallsItself()
	{
		string html = await RenderAsync(CatalogWith(1), (manager, workspace, _) =>
		{
			FakeSession session = manager.Add();
			workspace.GetViewState(session.Id, () => new SessionViewState());
			workspace.SetRemoteTitle(session.Id, "bc@build-01: /var/log");
		});

		Assert.Contains("bc@build-01: /var/log", html, StringComparison.Ordinal);
		Assert.Contains("What this session&#x27;s shell calls itself:", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_TheActiveSessionTypesWithAnother_WarnsAboutIt()
	{
		string html = await RenderAsync(CatalogWith(1), (manager, _, broadcast) =>
		{
			FakeSession first = manager.Add();
			FakeSession second = manager.Add();
			broadcast.Toggle(first.Id);
			broadcast.Toggle(second.Id);
		});

		Assert.Contains("TYPING INTO 2", html, StringComparison.Ordinal);
		Assert.Contains("also goes to 1 other session.", html, StringComparison.Ordinal);
	}

	// A group of one sends nowhere, so saying it types with others would be a lie.
	[Fact]
	public async Task Render_ABroadcastGroupOfOne_SaysNothing()
	{
		string html = await RenderAsync(CatalogWith(1), (manager, _, broadcast) =>
		{
			manager.Add();
			FakeSession active = manager.Add();
			broadcast.Toggle(active.Id);
		});

		Assert.DoesNotContain("TYPING INTO", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_AutoLockOff_SaysTheVaultNeverLocksByItself()
	{
		string html = await RenderAsync(ConnectionCatalog.Empty, security: new SecuritySettings { AutoLockMinutes = 0 });

		Assert.Contains("NO AUTO-LOCK", html, StringComparison.Ordinal);
		Assert.Contains("The vault never locks by itself.", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_DefaultAutoLock_SaysHowLongItTakes()
	{
		string html = await RenderAsync(ConnectionCatalog.Empty);

		Assert.Contains("AUTO-LOCK 15M", html, StringComparison.Ordinal);
		Assert.Contains("after 15 minutes without activity", html, StringComparison.Ordinal);
	}

	// Only the items that do something are buttons, and every item says what it is, whether it acts or not.
	[Fact]
	public async Task Render_OnlyItemsThatActAreButtons_AndEveryItemHasATooltip()
	{
		string html = await RenderAsync(CatalogWith(2), (manager, _, _) => manager.Add());

		List<string> items = [.. StatusBarItem().Matches(html).Select(match => match.Value)];
		Assert.NotEmpty(items);
		foreach (string item in items)
		{
			Match title = TitleAttribute().Match(item);
			Assert.True(title.Success && title.Groups[1].Value.Length > 0, item);
			Assert.Equal(item.Contains("role=\"button\"", StringComparison.Ordinal), item.Contains("tabindex=\"0\"", StringComparison.Ordinal));
		}

		// The connections panel, the transfers panel, locking the vault, and the about page.
		Assert.Equal(4, items.Count(item => item.Contains("role=\"button\"", StringComparison.Ordinal)));
	}

	private static ConnectionCatalog CatalogWith(int hosts)
	{
		List<HostProfile> profiles = [];
		List<ConnectionProfile> logins = [];
		for (int i = 0; i < hosts; i++)
		{
			Guid id = new([.. HostId.ToByteArray()[..15], (byte)i]);
			profiles.Add(new HostProfile { Id = id, Name = $"host-{i}", Address = $"10.0.0.{i}" });
			logins.Add(new ConnectionProfile { Id = Guid.NewGuid(), HostId = id, ProtocolId = "ssh", Username = "bc" });
		}

		return new ConnectionCatalog([], profiles, logins);
	}

	/// <summary>Renders the bar over <paramref name="catalog"/>, with <paramref name="arrange"/> opening what it needs first.</summary>
	private static async Task<string> RenderAsync(
		ConnectionCatalog catalog,
		Action<ManualSessionManager, SessionWorkspace, InputBroadcast>? arrange = null,
		SecuritySettings? security = null)
	{
		await using UiStateStore uiState = new(new MemoryAppDataStore(), TimeProvider.System, NullLogger<UiStateStore>.Instance);
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		using InputBroadcast broadcast = new(workspace);
		FixedSettings settings = security is null ? new() : new(security);
		arrange?.Invoke(manager, workspace, broadcast);

		return await StaticRender.RenderAsync<ShellStatusBar>(
			services =>
			{
				services.AddMokaRed();
				services.AddMokatermUiCommon();
				services.AddSingleton<IVault, UnlockedVault>();
				services.AddSingleton<IConnectionRepository>(new FixedCatalog(catalog));
				services.AddSingleton<ISettingsService>(settings);
				services.AddSingleton<ISessionManager>(manager);
				services.AddSingleton<ITransferQueue, IdleTransfers>();
				services.AddSingleton<IAppEnvironment, TestEnvironment>();
				services.AddSingleton(workspace);
				services.AddSingleton(broadcast);
				services.AddSingleton(uiState);
				services.AddScoped<ShellState>();
				services.AddScoped<CatalogState>();
			},
			new Dictionary<string, object?>(StringComparer.Ordinal)).WaitAsync(Patience, Ct);
	}

	[GeneratedRegex("<div class=\"moka-statusbar-item[^>]*>")]
	private static partial Regex StatusBarItem();

	[GeneratedRegex("title=\"([^\"]*)\"")]
	private static partial Regex TitleAttribute();

	/// <summary>The catalog the bar counts, handed over as it is.</summary>
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

	/// <summary>Unlocked, so the catalog loads; a static render can never press the lock.</summary>
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

	private sealed class IdleTransfers : ITransferQueue
	{
		public event Action? Changed
		{
			add { }
			remove { }
		}

		public IReadOnlyList<ITransferItem> Items => [];

		public int ActiveCount => 0;

		public ITransferItem Enqueue(TransferRequest request) => throw new NotSupportedException();

		public void Cancel(Guid id) => throw new NotSupportedException();

		public void Retry(Guid id) => throw new NotSupportedException();

		public void Remove(Guid id) => throw new NotSupportedException();

		public void ClearFinished() => throw new NotSupportedException();
	}

	private sealed class TestEnvironment : IAppEnvironment
	{
		public HostKind Kind => HostKind.Web;

		public string PlatformName => "Web";

		public string AppVersion => "9.9.9";

		public string DataDirectory => Path.Combine("C:", "mokaterm", "data");

		public string TempDirectory => Path.Combine("C:", "mokaterm", "temp");
	}
}
