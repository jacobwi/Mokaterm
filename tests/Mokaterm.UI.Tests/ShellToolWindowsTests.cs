using Mokaterm.Abstractions.Settings;
using Microsoft.Extensions.DependencyInjection;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Common.ToolPages;
using Mokaterm.UI.Shell;
using Mokaterm.UI.Tests.Fakes;

namespace Mokaterm.UI.Tests;

/// <summary>
/// What a tool's window is remembered with: its place and whether it stays on top, per tool rather than per session, and
/// an app tool apart from a session tool that happens to share its name.
/// </summary>
public sealed class ShellToolWindowsTests
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

	private static readonly SessionToolDescriptor Docker = new()
	{
		ProtocolId = "ssh",
		Id = "docker",
		Title = "Docker",
		Icon = MokatermIcons.Modules,
		Component = typeof(NoView),
		Size = SessionToolSize.Wide,
	};

	// The same name as the session tool on purpose: the two must not share a place.
	private static readonly AppToolDescriptor DockerHub = new()
	{
		Id = "docker",
		Title = "Docker Hub search",
		Icon = MokatermIcons.Globe,
		Component = typeof(NoAppTool),
	};

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task AWindowsPlace_IsRememberedPerTool_AndOffered_ToTheNextOne()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await CreateAsync(windows);
		FakeSession session = harness.Sessions.Add();
		await harness.SessionTools.OpenAsync(session, Docker).WaitAsync(Patience, Ct);

		ToolWindowBounds moved = new(40, 60, 1000, 700);
		windows.Opened[0].BoundsChanged!(moved);
		await harness.SessionTools.OpenAsync(harness.Sessions.Add(), Docker).WaitAsync(Patience, Ct);

		Assert.Equal(moved, harness.UiState.Current.ToolWindows["docker"]);
		Assert.Equal(moved, windows.Opened[1].Bounds);
	}

	[Fact]
	public async Task AnAppToolsPlace_IsKeptApartFromASessionToolOfTheSameName()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await CreateAsync(windows);
		FakeSession session = harness.Sessions.Add();
		await harness.SessionTools.OpenAsync(session, Docker).WaitAsync(Patience, Ct);
		await harness.AppTools.OpenAsync(DockerHub).WaitAsync(Patience, Ct);

		windows.Opened[0].BoundsChanged!(new ToolWindowBounds(0, 0, 1100, 800));
		windows.Opened[1].BoundsChanged!(new ToolWindowBounds(500, 300, 640, 480));

		Assert.Equal(new ToolWindowBounds(0, 0, 1100, 800), harness.UiState.Current.ToolWindows["docker"]);
		Assert.Equal(new ToolWindowBounds(500, 300, 640, 480), harness.UiState.Current.ToolWindows["app:docker"]);
	}

	[Fact]
	public async Task KeepOnTop_IsRememberedWithThePlace_AndTheNextWindowStaysOnTopToo()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await CreateAsync(windows);
		await harness.AppTools.OpenAsync(DockerHub).WaitAsync(Patience, Ct);
		ToolWindowTarget target = ToolWindowTarget.AppTool("docker");

		harness.Windows.SetKeepOnTop(target, true);

		Assert.True(harness.Windows.IsKeptOnTop(target));
		Assert.True(harness.UiState.Current.ToolWindows["app:docker"].KeepOnTop);

		windows.CloseByUser(target);
		await harness.AppTools.OpenAsync(DockerHub).WaitAsync(Patience, Ct);

		Assert.True(windows.Opened[1].Bounds?.KeepOnTop);
		Assert.True(harness.Windows.IsKeptOnTop(target));
	}

	[Fact]
	public async Task APlaceThatCannotBeAWindow_IsNotOffered()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await ToolHarness.CreateAsync(
			windows,
			saved: new UiStateDocument
			{
				ToolWindows = new Dictionary<string, ToolWindowBounds>(StringComparer.Ordinal)
				{
					["app:docker"] = new ToolWindowBounds(0, 0, 12, double.NaN) { KeepOnTop = true },
				},
			},
			appTools: [DockerHub]);

		await harness.AppTools.OpenAsync(DockerHub).WaitAsync(Patience, Ct);

		Assert.Null(Assert.Single(windows.Opened).Bounds);
	}

	[Fact]
	public async Task RememberingPlacesOff_OffersNoPlace_KeepsNone_AndDropsWhatWasKept()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await ToolHarness.CreateAsync(
			windows,
			new AppearanceSettings { RememberToolWindowPlaces = false },
			saved: new UiStateDocument
			{
				ToolWindows = new Dictionary<string, ToolWindowBounds>(StringComparer.Ordinal)
				{
					["app:docker"] = new ToolWindowBounds(40, 60, 1000, 700) { KeepOnTop = true },
				},
			},
			appTools: [DockerHub]);

		await harness.AppTools.OpenAsync(DockerHub).WaitAsync(Patience, Ct);
		harness.Windows.SetKeepOnTop(ToolWindowTarget.AppTool("docker"), true);

		ToolWindowRequest request = Assert.Single(windows.Opened);
		Assert.Null(request.Bounds);
		Assert.Null(request.BoundsChanged);
		Assert.Empty(harness.UiState.Current.ToolWindows);

		// The window itself still stays on top for as long as it is open.
		Assert.True(harness.Windows.IsKeptOnTop(ToolWindowTarget.AppTool("docker")));
	}

	// Turned off while a window is open: what that window reports when it closes is not kept either.
	[Fact]
	public async Task RememberingPlacesTurnedOff_WhileAWindowIsOpen_KeepsNothingItReports()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await CreateAsync(windows);
		await harness.AppTools.OpenAsync(DockerHub).WaitAsync(Patience, Ct);

		await harness.Settings.UpdateAsync<AppearanceSettings>(s => s with { RememberToolWindowPlaces = false }, Ct);
		windows.Opened[0].BoundsChanged!(new ToolWindowBounds(40, 60, 1000, 700));

		Assert.Empty(harness.UiState.Current.ToolWindows);
	}

	// The desktop's process ends right after its window closes, which is when a tool window reports where it was.
	[Fact]
	public async Task APlaceReportedAsTheAppCloses_IsWrittenByTheShutdownFlush_NotLeftForTheDelay()
	{
		FakeToolWindows windows = new();
		await using ToolHarness harness = await CreateAsync(windows);
		await harness.AppTools.OpenAsync(DockerHub).WaitAsync(Patience, Ct);
		ToolWindowBounds last = new(300, 200, 900, 640) { KeepOnTop = true };

		windows.Opened[0].BoundsChanged!(last);

		ServiceCollection page = new();
		page.AddSingleton(harness.UiState);
		await using ServiceProvider services = page.BuildServiceProvider();
		await ShellPersistence.FlushAsync(services).WaitAsync(Patience, Ct);

		UiStateDocument? written = await harness.Documents.ReadAsync<UiStateDocument>("ui-state", Ct);
		Assert.Equal(last, written?.ToolWindows["app:docker"]);
	}

	// The web host: nothing to keep on top, nothing to bring forward, and no window ever asked for.
	[Fact]
	public async Task NoWindowHost_AnswersEverythingWithNo()
	{
		await using ToolHarness harness = await CreateAsync(windows: null);
		ToolWindowTarget target = ToolWindowTarget.AppTool("docker");

		harness.Windows.SetKeepOnTop(target, true);
		harness.Windows.ShowOwner();
		harness.Windows.Close(target, byUser: true);

		Assert.False(harness.Windows.CanOpenWindows);
		Assert.False(harness.Windows.OpensWindows);
		Assert.False(harness.Windows.IsKeptOnTop(target));
		Assert.False(await harness.Windows.OpenAsync(target, "Docker Hub search · Mokaterm", SessionToolSize.Standard).WaitAsync(Patience, Ct));
	}

	private static Task<ToolHarness> CreateAsync(FakeToolWindows? windows) =>
		ToolHarness.CreateAsync(windows, new AppearanceSettings(), sessionTools: [Docker], appTools: [DockerHub]);

	private sealed class NoView : SessionViewBase;

	private sealed class NoAppTool : AppToolBase;
}
