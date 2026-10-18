using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Fleet;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.ServerAdmin;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Transfers;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Connections;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;
using Mokaterm.UI.Tools;
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

	// The shell knows nothing of SSH: whatever the session's module lists is drawn, one chip each, with its sentence.
	[Fact]
	public async Task Render_TheSessionReportsIndicators_ShowsAChipForEach()
	{
		string html = await RenderAsync(CatalogWith(1), (manager, _, _) => manager.Add(new FakeSession
		{
			Session = new IndicatingSession(
				new SessionIndicator("AGENT", SessionIndicatorState.On, "Agent forwarding is on."),
				new SessionIndicator("X11", SessionIndicatorState.Problem, "X11 forwarding is not working.")),
		}));

		Assert.Contains("mt-status-chip mt-status-chip--on", html, StringComparison.Ordinal);
		Assert.Contains("mt-status-chip mt-status-chip--problem", html, StringComparison.Ordinal);
		Assert.Contains(">AGENT", html, StringComparison.Ordinal);
		Assert.Contains("aria-description=\"X11 forwarding is not working.\"", html, StringComparison.Ordinal);
		Assert.Equal("top", Tooltips.PositionOf(html, "X11 forwarding is not working."));
	}

	[Fact]
	public async Task Render_ASessionWithoutIndicators_ShowsNoChip()
	{
		string html = await RenderAsync(CatalogWith(1), (manager, _, _) => manager.Add());

		Assert.DoesNotContain("mt-status-chip", html, StringComparison.Ordinal);
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

	// Only the items that do something are buttons, and every item says what it is, whether it acts or not: in Moka's
	// tooltip, above it since the bar is at the bottom of the window, and as its description for a screen reader.
	[Fact]
	public async Task Render_OnlyItemsThatActAreButtons_AndEveryItemHasATooltip()
	{
		string html = await RenderAsync(CatalogWith(2), (manager, _, _) => manager.Add());

		List<string> items = [.. StatusBarItem().Matches(html).Select(match => match.Value)];
		Assert.NotEmpty(items);
		foreach (string item in items)
		{
			Match description = DescriptionAttribute().Match(item);
			Assert.True(description.Success && description.Groups[1].Value.Length > 0, item);
			string tooltip = WebUtility.HtmlDecode(description.Groups[1].Value);
			Assert.StartsWith(item, Tooltips.ControlFor(html, tooltip).TrimStart(), StringComparison.Ordinal);
			Assert.Equal("top", Tooltips.PositionOf(html, tooltip));
			Assert.Equal(item.Contains("role=\"button\"", StringComparison.Ordinal), item.Contains("tabindex=\"0\"", StringComparison.Ordinal));
		}

		// The connections panel, the transfers panel, locking the vault, and the about page.
		Assert.Equal(4, items.Count(item => item.Contains("role=\"button\"", StringComparison.Ordinal)));
	}

	[Fact]
	public async Task Render_SplitWorkspace_SaysHowManyPanes()
	{
		string html = await RenderAsync(CatalogWith(1), (manager, workspace, _) =>
		{
			manager.Add();
			manager.Add();
			manager.Add();
			workspace.Arrange(PaneArrangement.MainAndStack);
		});

		Assert.Contains("3 sessions are open. They are split over 3 panes.", html, StringComparison.Ordinal);
	}

	// The shell knows nothing of SSH: a session that offers live figures gets three short shares, coloured by the usage
	// level rule, which a click opens in the tool they name.
	[Fact]
	public async Task Render_TheSessionHasFigures_ShowsThemByLevel_AndOpensTheirTool()
	{
		FakeHostMonitor monitor = new(new HostMonitorState { Figures = Figures(cpu: 12, memory: 80, disk: 95) });

		string html = await RenderAsync(CatalogWith(1), (manager, _, _) => manager.Add(new FakeSession { Session = FakeHostMonitor.SessionWith(monitor) }));

		Assert.Matches("mt-host-figure-label\"[^>]*>CPU</span>\\s*<span class=\"mt-host-figure-value\"[^>]*>12%</span>", html);
		Assert.Contains("class=\"mt-host-figure mt-host-figure--warn\"", html, StringComparison.Ordinal);
		Assert.Contains("class=\"mt-host-figure mt-host-figure--full\"", html, StringComparison.Ordinal);
		string tooltip = Assert.Single(Tooltips.Texts(html), text => text.StartsWith("CPU 12% busy", StringComparison.Ordinal));
		Assert.EndsWith("Click to open the System tool.", tooltip, StringComparison.Ordinal);
		Assert.Equal("top", Tooltips.PositionOf(html, tooltip));
		Assert.Contains("role=\"button\"", Tooltips.ControlFor(html, tooltip), StringComparison.Ordinal);

		// Leased once, at the setting's default pace, and let go when the bar went away.
		Assert.Equal([TimeSpan.FromSeconds(AppearanceSettings.DefaultHostFiguresSeconds)], monitor.Asked);
		Assert.Empty(monitor.Leases);
		Assert.Equal(0, monitor.Subscribers);
	}

	[Fact]
	public async Task Render_FiguresNamingNoToolOfTheProtocol_AreNotAButton()
	{
		FakeHostMonitor monitor = new(new HostMonitorState { Figures = Figures(cpu: 12, memory: 40, disk: 50) }, toolKey: "elsewhere");

		string html = await RenderAsync(CatalogWith(1), (manager, _, _) => manager.Add(new FakeSession { Session = FakeHostMonitor.SessionWith(monitor) }));

		string tooltip = Assert.Single(Tooltips.Texts(html), text => text.StartsWith("CPU 12% busy", StringComparison.Ordinal));
		Assert.DoesNotContain("Click", tooltip, StringComparison.Ordinal);
		Assert.DoesNotContain("role=\"button\"", Tooltips.ControlFor(html, tooltip), StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_TwoSessions_WatchesTheActiveOneOnly()
	{
		FakeHostMonitor first = new(new HostMonitorState { Figures = Figures(cpu: 1, memory: 1, disk: 1) });
		FakeHostMonitor second = new(new HostMonitorState { Figures = Figures(cpu: 22, memory: 33, disk: 44) });
		ISessionHandle? active = null;

		string html = await RenderAsync(CatalogWith(1), (manager, workspace, _) =>
		{
			manager.Add(new FakeSession { Session = FakeHostMonitor.SessionWith(first) });
			manager.Add(new FakeSession { Session = FakeHostMonitor.SessionWith(second) });
			active = workspace.Active;
		});

		FakeHostMonitor watched = ReferenceEquals(active?.Session?.GetFeature<IHostMonitorFeature>(), first) ? first : second;
		FakeHostMonitor other = ReferenceEquals(watched, first) ? second : first;
		Assert.Single(watched.Asked);
		Assert.Empty(other.Asked);
		Assert.Contains(ReferenceEquals(watched, second) ? ">22%<" : ">1%<", html, StringComparison.Ordinal);
	}

	// Off in settings, a page nobody sees, a dropped session: nothing is read for any of them.
	[Fact]
	public async Task Render_FiguresTurnedOff_ReadsNothing()
	{
		FakeHostMonitor monitor = new(new HostMonitorState { Figures = Figures(cpu: 12, memory: 40, disk: 50) });

		string html = await RenderAsync(
			CatalogWith(1),
			(manager, _, _) => manager.Add(new FakeSession { Session = FakeHostMonitor.SessionWith(monitor) }),
			appearance: new AppearanceSettings { ShowHostFigures = false });

		Assert.DoesNotContain("mt-host-figures", html, StringComparison.Ordinal);
		Assert.Empty(monitor.Asked);
	}

	[Fact]
	public async Task Render_AHiddenPage_ReadsNothing()
	{
		FakeHostMonitor monitor = new(new HostMonitorState { Figures = Figures(cpu: 12, memory: 40, disk: 50) });
		PageVisibility hidden = new();
		hidden.Set(false);

		string html = await RenderAsync(
			CatalogWith(1),
			(manager, _, _) => manager.Add(new FakeSession { Session = FakeHostMonitor.SessionWith(monitor) }),
			visibility: hidden);

		Assert.DoesNotContain("mt-host-figures", html, StringComparison.Ordinal);
		Assert.Empty(monitor.Asked);
	}

	[Fact]
	public async Task Render_ADroppedSession_ReadsNothing()
	{
		FakeHostMonitor monitor = new(new HostMonitorState { Figures = Figures(cpu: 12, memory: 40, disk: 50) });

		string html = await RenderAsync(CatalogWith(1), (manager, _, _) =>
			manager.Add(new FakeSession { Session = FakeHostMonitor.SessionWith(monitor) }.Become(SessionState.Disconnected)));

		Assert.DoesNotContain("mt-host-figures", html, StringComparison.Ordinal);
		Assert.Empty(monitor.Asked);
	}

	[Fact]
	public async Task Render_TheIntervalSetting_IsThePaceAskedFor()
	{
		FakeHostMonitor monitor = new(new HostMonitorState { Figures = Figures(cpu: 12, memory: 40, disk: 50) });

		await RenderAsync(
			CatalogWith(1),
			(manager, _, _) => manager.Add(new FakeSession { Session = FakeHostMonitor.SessionWith(monitor) }),
			appearance: new AppearanceSettings { HostFiguresSeconds = 12 });

		Assert.Equal([TimeSpan.FromSeconds(12)], monitor.Asked);
	}

	// Watched, but nothing to show: before the first read, after several failed ones, or on a login that cannot give any.
	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	public async Task Render_NothingWorthShowing_ShowsNoItem(int which)
	{
		HostMonitorState state = which switch
		{
			0 => HostMonitorState.Waiting,
			1 => new HostMonitorState { Figures = Figures(cpu: 12, memory: 40, disk: 50), FailedReads = HostMonitorState.HideAfterFailures },
			_ => new HostMonitorState { Unavailable = "This login does not run scripts." },
		};
		FakeHostMonitor monitor = new(state);

		string html = await RenderAsync(CatalogWith(1), (manager, _, _) => manager.Add(new FakeSession { Session = FakeHostMonitor.SessionWith(monitor) }));

		Assert.DoesNotContain("mt-host-figures", html, StringComparison.Ordinal);
		Assert.Single(monitor.Asked);
	}

	[Fact]
	public async Task Render_AfterAFailedRead_ShowsTheOldFiguresDimmed_AndSaysSo()
	{
		FakeHostMonitor monitor = new(new HostMonitorState
		{
			Figures = Figures(cpu: 12, memory: 40, disk: 50),
			FailedReads = 1,
			Problem = "The connection closed before the command finished.",
		});

		string html = await RenderAsync(CatalogWith(1), (manager, _, _) => manager.Add(new FakeSession { Session = FakeHostMonitor.SessionWith(monitor) }));

		Assert.Contains("class=\"mt-host-figures mt-host-figures--stale\"", html, StringComparison.Ordinal);
		Assert.Contains("the read after them failed. The connection closed before the command finished.", html, StringComparison.Ordinal);
	}

	private static HostFigures Figures(double cpu, double memory, int disk) => new()
	{
		ReadAt = DateTimeOffset.UnixEpoch,
		CpuPercent = cpu,
		MemoryUsedBytes = (long)(memory * 1024 * 1024),
		MemoryTotalBytes = 100L * 1024 * 1024,
		RootDisk = new HostDiskUsage { MountPoint = "/", UsedBytes = disk, TotalBytes = 100, UsedPercent = disk },
	};

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
		SecuritySettings? security = null,
		AppearanceSettings? appearance = null,
		PageVisibility? visibility = null)
	{
		await using UiStateStore uiState = new(new MemoryAppDataStore(), TimeProvider.System, NullLogger<UiStateStore>.Instance);
		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		using InputBroadcast broadcast = new(workspace);
		FixedSettings settings = new([.. new object?[] { security, appearance }.OfType<object>()]);
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
				services.AddSingleton(visibility ?? new PageVisibility());
				services.AddScoped<UserInteractionService>();
				services.AddScoped<ShellToolWindows>();
				services.AddScoped<ISessionTabs, ToolHarness.RecordingTabs>();
				services.AddScoped<SessionToolActions>();
				services.AddSessionTool(new SessionToolDescriptor
				{
					ProtocolId = "ssh",
					Id = "system",
					Title = "System",
					Icon = MokatermIcons.Server,
					Component = typeof(NoView),
				});
			},
			new Dictionary<string, object?>(StringComparer.Ordinal)).WaitAsync(Patience, Ct);
	}

	[GeneratedRegex("<div class=\"moka-statusbar-item[^>]*>")]
	private static partial Regex StatusBarItem();

	[GeneratedRegex("aria-description=\"([^\"]*)\"")]
	private static partial Regex DescriptionAttribute();

	private sealed class NoView : SessionViewBase;

	/// <summary>A live session whose only feature is the indicators it was given.</summary>
	private sealed class IndicatingSession(params SessionIndicator[] indicators) : IProtocolSession, ISessionIndicators
	{
		public event Action? Changed
		{
			add { }
			remove { }
		}

		public Task Completion => Task.CompletedTask;

		public IReadOnlyList<SessionIndicator> Indicators => indicators;

		public TFeature? GetFeature<TFeature>() where TFeature : class => this as TFeature;

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}

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
