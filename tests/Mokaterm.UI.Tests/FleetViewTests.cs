using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Fleet;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Connections;
using Mokaterm.UI.Fleet;
using Mokaterm.UI.Interaction;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;
using Mokaterm.UI.Workspace;

namespace Mokaterm.UI.Tests;

/// <summary>What the fleet overview lists, what it says about each machine, and what the window keeps of a read.</summary>
public sealed class FleetViewTests
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

	private static readonly ProtocolDescriptor Ssh = new()
	{
		Id = "ssh",
		DisplayName = "SSH",
		DefaultPort = 22,
		Capabilities = ProtocolCapabilities.Terminal | ProtocolCapabilities.Commands | ProtocolCapabilities.HostSummary,
	};

	private static readonly ProtocolDescriptor Sftp = new()
	{
		Id = "sftp",
		DisplayName = "SFTP",
		DefaultPort = 22,
		Capabilities = ProtocolCapabilities.FileSystem,
		VariantOf = "ssh",
	};

	private static readonly HostProfile Web = new() { Id = Guid.NewGuid(), Name = "web01", Address = "10.0.0.20" };
	private static readonly HostProfile Db = new() { Id = Guid.NewGuid(), Name = "db", Address = "10.0.0.10" };
	private static readonly HostProfile Nas = new() { Id = Guid.NewGuid(), Name = "nas", Address = "10.0.0.30" };
	private static readonly ConnectionProfile WebRoot = Login(Web, "ssh", "root");
	private static readonly ConnectionProfile DbOps = Login(Db, "ssh", "ops");
	private static readonly ConnectionProfile DbFiles = Login(Db, "sftp", "files");
	private static readonly ConnectionProfile NasAdmin = Login(Nas, "ssh", "admin");

	private static readonly ConnectionCatalog Catalog = new([], [Web, Db, Nas], [WebRoot, DbOps, DbFiles, NasAdmin]);

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task BeforeAnyRead_ListsTheLoginsThatCanReport_AndSaysNothingWasRead()
	{
		string html = await RenderAsync(new FakeSurveyRunner(), read: false);

		Assert.Contains("Fleet overview", html, StringComparison.Ordinal);
		Assert.Contains("10.0.0.20", html, StringComparison.Ordinal);
		Assert.Contains("10.0.0.10", html, StringComparison.Ordinal);
		Assert.DoesNotContain(">files<", html, StringComparison.Ordinal);
		Assert.Contains("NOT READ", html, StringComparison.Ordinal);
		Assert.Contains("Nothing read yet.", html, StringComparison.Ordinal);
		Assert.Contains("3 logins", html, StringComparison.Ordinal);

		// Machines in name order, as the tree has them: db, nas, web01.
		Assert.True(html.IndexOf("10.0.0.10", StringComparison.Ordinal) < html.IndexOf("10.0.0.30", StringComparison.Ordinal));
		Assert.True(html.IndexOf("10.0.0.30", StringComparison.Ordinal) < html.IndexOf("10.0.0.20", StringComparison.Ordinal));
		Assert.Contains("role=\"grid\"", html, StringComparison.Ordinal);
		Assert.Contains("data-mt-row", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AfterARead_ShowsEachMachine_AndWhatNeedsALook()
	{
		FakeSurveyRunner runner = new()
		{
			Results =
			[
				Result(WebRoot, HostRunState.Done, Busy()),
				Result(DbOps, HostRunState.Skipped) with { Message = "It would ask about its host key, which is not trusted yet." },
				Result(NasAdmin, HostRunState.Failed, Busy()) with { Message = "Connecting took longer than 60 s." },
			],
		};

		string html = await RenderAsync(runner, read: true);

		Assert.Contains("Ubuntu 24.04.1 LTS", html, StringComparison.Ordinal);
		Assert.Contains("12d 0h", html, StringComparison.Ordinal);
		Assert.Contains("92%", html, StringComparison.Ordinal);
		Assert.Contains("2 SEC", html, StringComparison.Ordinal);
		Assert.Contains("REBOOT", html, StringComparison.Ordinal);
		Assert.Contains("6/8", html, StringComparison.Ordinal);
		Assert.Contains("role=\"meter\"", html, StringComparison.Ordinal);
		Assert.Contains("SKIPPED", html, StringComparison.Ordinal);

		// A login that was never read says why in its row, not only in its details.
		Assert.Contains("It would ask about its host key, which is not trusted yet.</span>", html, StringComparison.Ordinal);
		Assert.Contains("FAILED", html, StringComparison.Ordinal);
		Assert.Contains("Run skipped, asking (1)", html, StringComparison.Ordinal);
		Assert.Contains("Retry failed (1)", html, StringComparison.Ordinal);
		Assert.Contains("Needs a look (3)", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Nothing read yet.", html, StringComparison.Ordinal);

		// The failed row keeps the summary of its earlier read, and looks stale.
		Assert.Contains("mt-fleet-row--stale", html, StringComparison.Ordinal);
	}

	[Fact]
	public void Attention_NamesWhatIsWrong_MostPressingFirst()
	{
		FleetRow busy = new(Web, WebRoot, Result(WebRoot, HostRunState.Done, Busy()));
		FleetRow fine = new(Web, WebRoot, Result(WebRoot, HostRunState.Done, new HostSummary { FailedServices = 0, RebootRequired = false }));
		FleetRow skipped = new(Db, DbOps, Result(DbOps, HostRunState.Skipped) with { Message = "It would ask for the login." });

		Assert.Equal(
			["1 failed service", "Reboot required", "/srv/data is 92% full", "Load 6.20 on 4 CPUs", "2 security updates"],
			FleetAttention.Reasons(busy));
		Assert.Empty(FleetAttention.Reasons(fine));
		Assert.Equal(["It would ask for the login."], FleetAttention.Reasons(skipped));
		Assert.Empty(FleetAttention.Reasons(new FleetRow(Nas, NasAdmin, null)));
	}

	[Fact]
	public async Task Overview_StartsOneSurvey_ThenRefreshesIt_AlwaysSkippingHostsThatAsk()
	{
		FakeSurveyRunner runner = new();
		using FleetOverview overview = CreateOverview(runner, new UnlockedVault(), new CommandRunSettings { SkipHostsThatAsk = false, ParallelHosts = 3 });

		Assert.Equal(3, await overview.RefreshAsync().WaitAsync(Patience, Ct));
		Assert.Equal(0, await overview.RefreshAsync([WebRoot.Id]).WaitAsync(Patience, Ct));

		HostSurveyRequest request = Assert.Single(runner.Requests);
		Assert.Equal([DbOps.Id, NasAdmin.Id, WebRoot.Id], request.ConnectionIds);
		Assert.True(request.Options.SkipHostsThatAsk);
		Assert.Equal(3, request.Options.ParallelHosts);
		Assert.Equal([WebRoot.Id], Assert.Single(runner.Survey!.Refreshes));
		Assert.True(overview.HasSurvey);
	}

	[Fact]
	public async Task Overview_DropsEverythingItReadWhenTheVaultLocks()
	{
		FakeSurveyRunner runner = new() { Results = [Result(WebRoot, HostRunState.Done, Busy())] };
		LockableVault vault = new();
		using FleetOverview overview = CreateOverview(runner, vault, new CommandRunSettings());
		_ = await overview.RefreshAsync().WaitAsync(Patience, Ct);
		Assert.NotNull(overview.Rows().Single(row => row.Login.Id == WebRoot.Id).Summary);

		vault.Lock();

		Assert.False(overview.HasSurvey);
		await Eventually.TrueAsync(() => runner.Survey!.Disposed, "the survey and its connections are let go");
		Assert.Equal(0, await overview.RefreshAsync().WaitAsync(Patience, Ct));
		_ = Assert.Single(runner.Requests);
	}

	private static ConnectionProfile Login(HostProfile host, string protocolId, string user) => new()
	{
		Id = Guid.NewGuid(),
		HostId = host.Id,
		ProtocolId = protocolId,
		Username = user,
	};

	private static HostSurveyResult Result(ConnectionProfile login, HostRunState state, HostSummary? summary = null) => new()
	{
		ConnectionId = login.Id,
		Title = login.Username ?? "",
		HostName = "",
		Address = "",
		State = state,
		Summary = summary,
		ReadAt = summary is null ? null : new DateTimeOffset(2026, 10, 11, 8, 0, 0, TimeSpan.Zero),
		Attempt = 1,
	};

	private static HostSummary Busy() => new()
	{
		OperatingSystem = "Ubuntu 24.04.1 LTS",
		Kernel = "Linux 6.8.0-45-generic x86_64",
		Uptime = TimeSpan.FromDays(12),
		Load1 = 6.2,
		Load5 = 4.1,
		Load15 = 3.0,
		CpuCount = 4,
		MemoryUsedBytes = 4L << 30,
		MemoryTotalBytes = 8L << 30,
		FullestDisk = new HostDiskUsage { MountPoint = "/srv/data", UsedBytes = 184L << 30, TotalBytes = 200L << 30, UsedPercent = 92 },
		PendingUpdates = 4,
		SecurityUpdates = 2,
		RebootRequired = true,
		FailedServices = 1,
		FailedServiceNames = ["backup-agent"],
		ContainersRunning = 6,
		ContainersTotal = 8,
	};

	private static FleetOverview CreateOverview(FakeSurveyRunner runner, IVault vault, CommandRunSettings options) => new(
		runner,
		new CatalogState(new EditableCatalog(Catalog), vault, NullLogger<CatalogState>.Instance),
		new TwoProtocols(),
		new FixedSettings(options),
		vault,
		NullLogger<FleetOverview>.Instance);

	private static async Task<string> RenderAsync(FakeSurveyRunner runner, bool read)
	{
		MemoryAppDataStore store = new();
		await using UiStateStore uiState = new(store, TimeProvider.System, NullLogger<UiStateStore>.Instance);
		await uiState.LoadAsync(Ct).WaitAsync(Patience, Ct);

		ManualSessionManager manager = new();
		using SessionWorkspace workspace = new(manager);
		FixedSettings settings = new();
		using SessionRestore restore = new(manager, workspace, uiState, settings, new UserInteractionService(), NullLogger<SessionRestore>.Instance);
		ShellState shell = new(uiState);
		_ = shell.OpenEditor(new FleetEditorRequest());
		UnlockedVault vault = new();
		CatalogState catalog = new(new EditableCatalog(Catalog), vault, NullLogger<CatalogState>.Instance);
		using FleetOverview overview = new(runner, catalog, new TwoProtocols(), settings, vault, NullLogger<FleetOverview>.Instance);
		if (read)
		{
			_ = await overview.RefreshAsync().WaitAsync(Patience, Ct);
		}

		return await StaticRender.RenderAsync<FleetDialog>(
			services =>
			{
				services.AddMokaRed();
				services.AddMokatermUiCommon();
				services.AddSingleton(TimeProvider.System);
				services.AddSingleton<IVault>(vault);
				services.AddSingleton<IProtocolRegistry, TwoProtocols>();
				services.AddSingleton<ISettingsService>(settings);
				services.AddSingleton<ISessionManager>(manager);
				services.AddSingleton(workspace);
				services.AddSingleton(restore);
				services.AddSingleton(uiState);
				services.AddSingleton(shell);
				services.AddSingleton(catalog);
				services.AddSingleton(overview);
				services.AddScoped<IUserInteraction, UserInteractionService>();
				services.AddScoped<FormInterop>();
				services.AddScoped<ShellInterop>();
				services.AddScoped<SessionActions>();
			},
			new Dictionary<string, object?>(StringComparer.Ordinal)).WaitAsync(Patience, Ct);
	}

	private sealed class TwoProtocols : IProtocolRegistry
	{
		private readonly Dictionary<string, IProtocolProvider> _providers = new(StringComparer.OrdinalIgnoreCase)
		{
			["ssh"] = new DescriptorOnly(Ssh),
			["sftp"] = new DescriptorOnly(Sftp),
		};

		public IReadOnlyList<ProtocolDescriptor> Protocols => [Ssh, Sftp];

		public IProtocolProvider? Find(string protocolId) => _providers.GetValueOrDefault(protocolId);

		public IProtocolProvider Get(string protocolId) => _providers[protocolId];
	}

	private sealed class DescriptorOnly(ProtocolDescriptor descriptor) : IProtocolProvider
	{
		public ProtocolDescriptor Descriptor => descriptor;

		public Task<IProtocolSession> ConnectAsync(ProtocolConnectContext context, CancellationToken cancellationToken) =>
			throw new NotSupportedException("The overview never connects by itself.");
	}

	/// <summary>Starts a survey that already has the rows a test gave it, and records what it was asked for.</summary>
	private sealed class FakeSurveyRunner : IHostSurveyRunner
	{
		private readonly List<HostSurveyRequest> _requests = [];

		public IReadOnlyList<HostSurveyResult> Results { get; init; } = [];

		public IReadOnlyList<HostSurveyRequest> Requests => _requests;

		public FakeSurvey? Survey { get; private set; }

		public Task<IHostSurvey> StartAsync(HostSurveyRequest request, CancellationToken cancellationToken = default)
		{
			_requests.Add(request);
			Survey = new FakeSurvey(Results);
			return Task.FromResult<IHostSurvey>(Survey);
		}
	}

	private sealed class FakeSurvey(IReadOnlyList<HostSurveyResult> hosts) : IHostSurvey
	{
		private readonly List<IReadOnlyCollection<Guid>> _refreshes = [];

		public event Action? Changed
		{
			add { }
			remove { }
		}

		public IReadOnlyList<HostSurveyResult> Hosts => hosts;

		public bool IsRunning => false;

		public bool Disposed { get; private set; }

		public IReadOnlyList<IReadOnlyCollection<Guid>> Refreshes => _refreshes;

		public Task<int> RefreshAsync(IReadOnlyCollection<Guid> connectionIds, bool askPrompts = false, CancellationToken cancellationToken = default)
		{
			_refreshes.Add(connectionIds);
			return Task.FromResult(0);
		}

		public void CancelAll()
		{
		}

		public Task<int> RetryFailedAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

		public Task<int> RetrySkippedAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

		public Task WaitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public ValueTask DisposeAsync()
		{
			Disposed = true;
			return ValueTask.CompletedTask;
		}
	}
}
