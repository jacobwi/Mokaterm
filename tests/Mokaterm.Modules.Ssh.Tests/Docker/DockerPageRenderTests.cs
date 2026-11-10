using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Moka.Red.Extensions;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Modules.Ssh.Components.Docker;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Tests.Fakes;
using Mokaterm.UI.Common.Extensions;
using Mokaterm.UI.Common.Files;
using Mokaterm.UI.Common.Formatting;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

// Every list page of the manager draws its rows in a Moka table on the shared page frame, over a docker answered from a
// table of what docker 27 prints.
public sealed class DockerPageRenderTests
{
	/// <summary>Docker 27's <c>system df --verbose --format '{{json .}}'</c>, trimmed to a few of each.</summary>
	private const string Usage = """
		{"BuildCache":[{"CacheType":"regular","Description":"[build 3/6] RUN apt-get update","ID":"q2cm6v3xjqg5wzwn3bwt8hduq","InUse":"false","LastUsedSince":"6 days ago","Shared":"false","Size":"182MB"}],"Containers":[{"ID":"0a1b2c3d4e5f60718293a4b5c6d7e8f9","Image":"nginx:1.27-alpine","Labels":"","Names":"web","Size":"1.09kB","State":"running","Status":"Up 2 hours"},{"ID":"9f8e7d6c5b4a39281706f5e4d3c2b1a0","Image":"redis:7.4","Names":"redis","Size":"18.4kB","State":"exited","Status":"Exited (0) 3 days ago"},{"ID":"aabbccddeeff0011","Image":"alpine:3.20","Names":"seed","Size":"0B","State":"created","Status":"Created"}],"Images":[{"Containers":"0","CreatedSince":"9 days ago","ID":"sha256:dddd4444dddd4444dddd4444dddd4444dddd4444dddd4444dddd4444dddd4444","Repository":"<none>","Size":"198MB","Tag":"<none>","UniqueSize":"198MB"}],"Volumes":[{"Driver":"local","Labels":"","Links":"0","Name":"old-cache","Size":"212MB"}]}
		""";

	private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

	private readonly FakeTimeProvider _time = new(Now);
	private readonly FakeDockerShell _shell = new();
	private readonly ScriptedSessionHandle _session;

	public DockerPageRenderTests()
	{
		DockerSessionFeature feature = new(_shell, authenticate: null, _time, "build-server", "ada", files: null);
		_session = new ScriptedSessionHandle(new FeatureSession(feature));
		_shell
			.Answer(Line(DockerCommands.ListContainers()), 0, DockerFixtures.Containers)
			.Answer(Line(DockerCommands.ListImages()), 0, DockerFixtures.Images)
			.Answer(Line(DockerCommands.ListVolumes()), 0, DockerFixtures.Volumes)
			.Answer(Line(DockerCommands.UnusedVolumes()), 0, "old-cache\n")
			.Answer(Line(DockerCommands.ListNetworks()), 0, DockerFixtures.Networks)
			.Answer(Line(DockerCommands.Stats([])), 0, DockerFixtures.Stats)
			.Answer(Line(DockerCommands.DiskUsage()), 0, DockerFixtures.DiskUsage)
			.Answer(Line(DockerCommands.SystemUsage()), 0, Usage.ReplaceLineEndings(""));
	}

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task TheContainers_AreGroupedUnderTheirProject_WithStandaloneLast()
	{
		string html = await RenderAsync<DockerContainersPage>();

		Assert.Contains("<table", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Containers\"", html, StringComparison.Ordinal);
		int monitoring = html.IndexOf(">monitoring<", StringComparison.Ordinal);
		int shop = html.IndexOf(">shop<", StringComparison.Ordinal);
		int standalone = html.IndexOf(">Standalone<", StringComparison.Ordinal);
		Assert.True(monitoring >= 0 && monitoring < shop && shop < standalone, "the groups are the projects by name, then Standalone");

		// Within its project a container goes by the rest of its name, with the whole name in its title.
		int web = html.IndexOf(">web-1<", StringComparison.Ordinal);
		Assert.True(shop < web && web < standalone, "shop-web-1 sits in the shop group");
		Assert.Contains("title=\"shop-web-1\"", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Actions for shop-web-1\"", html, StringComparison.Ordinal);
		Assert.Contains("New container", html, StringComparison.Ordinal);

		// Every row can be picked for the bulk bar, and the columns can be chosen.
		Assert.Contains("aria-label=\"Select shop-web-1\"", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Toggle columns\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheImages_AreRowsOfATable_WithTheDanglingOneMarked()
	{
		string html = await RenderAsync<DockerImagesPage>();

		Assert.Contains("aria-label=\"Images\"", html, StringComparison.Ordinal);
		Assert.Contains(">nginx:1.27-alpine<", html, StringComparison.Ordinal);
		Assert.Contains(">dangling<", html, StringComparison.Ordinal);
		Assert.Contains("3 images, 1 tagged, 1 dangling", html, StringComparison.Ordinal);

		// Sizes are ours, in 1024s, with a bar of each one's share of the largest and the total in the foot.
		Assert.Contains($">{DisplayFormat.Bytes(48_300_000)}<", html, StringComparison.Ordinal);
		Assert.DoesNotContain("48.3MB", html, StringComparison.Ordinal);
		Assert.Equal(3, Count(html, "mt-dk-size-bar"));
		Assert.Contains($"{DisplayFormat.Bytes(48_300_000 + 198_000_000 + 1_200_000_000)} in all", html, StringComparison.Ordinal);

		// The tag's container, read from the container listing, and the rest unused.
		Assert.Contains(">shop-web-1<", html, StringComparison.Ordinal);
		Assert.Equal(2, Count(html, ">unused<"));
		Assert.Contains("aria-label=\"Select nginx:1.27-alpine\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheVolumes_SayWhichOnesAreInUse()
	{
		string html = await RenderAsync<DockerVolumesPage>();

		Assert.Contains("aria-label=\"Volumes\"", html, StringComparison.Ordinal);
		Assert.Contains(">shop_pgdata<", html, StringComparison.Ordinal);
		Assert.Contains(">old-cache<", html, StringComparison.Ordinal);
		Assert.Contains(">unused<", html, StringComparison.Ordinal);

		// shop_pgdata names the container that mounts it, and old-cache its size from system df --verbose.
		Assert.Contains(">shop-db-1<", html, StringComparison.Ordinal);
		Assert.Contains($">{DisplayFormat.Bytes(212_000_000)}<", html, StringComparison.Ordinal);
		Assert.Contains("Measure again", html, StringComparison.Ordinal);
		Assert.Single(_shell.Asked, line => line == Line(DockerCommands.SystemUsage()));
	}

	[Fact]
	public async Task TheVolumes_AreNotMeasuredAgain_WhileTheLastReadIsRecent()
	{
		await RenderAsync<DockerVolumesPage>();
		await RenderAsync<DockerVolumesPage>();

		Assert.Single(_shell.Asked, line => line == Line(DockerCommands.SystemUsage()));
	}

	[Fact]
	public async Task TheNetworks_MarkDockersOwn_AndOfferToRemoveTheUnusedOne()
	{
		string html = await RenderAsync<DockerNetworksPage>();

		Assert.Contains("aria-label=\"Networks\"", html, StringComparison.Ordinal);
		Assert.Contains(">legacy_net<", html, StringComparison.Ordinal);
		Assert.Contains(">built in<", html, StringComparison.Ordinal);
		Assert.Contains("Remove unused (1)", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheStats_ListEverySampledContainer()
	{
		string html = await RenderAsync<DockerStatsPage>();

		Assert.Contains("aria-label=\"Container figures\"", html, StringComparison.Ordinal);
		Assert.Contains(">shop-web-1<", html, StringComparison.Ordinal);
		Assert.Contains("0.52%", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Recent CPU of shop-web-1\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheStats_PutTilesAndLiveCharts_OverTheTable()
	{
		string html = await RenderAsync<DockerStatsPage>();

		Assert.Contains("moka-stat--tile", html, StringComparison.Ordinal);
		Assert.Contains(">Running<", html, StringComparison.Ordinal);
		Assert.Contains("10.2 MB", html, StringComparison.Ordinal);
		Assert.Equal(4, Count(html, "class=\"moka-linechart-plot\""));
		Assert.Contains(">CPU by container<", html, StringComparison.Ordinal);
		Assert.Contains(">Network, all shown<", html, StringComparison.Ordinal);

		// One sample is all there is, so the rates wait for the next one rather than reading zero.
		Assert.Contains("Waiting for the next sample", html, StringComparison.Ordinal);
		Assert.True(
			html.IndexOf(">CPU by container<", StringComparison.Ordinal) < html.IndexOf("aria-label=\"Container figures\"", StringComparison.Ordinal),
			"the charts come before the table");
	}

	[Fact]
	public async Task AContainersStatsTab_DrawsItsTiles_AndFourLiveCharts()
	{
		_shell.Answer(Line(DockerCommands.Stats(["0a1b2c3d4e5f"])), 0, DockerFixtures.Stats.Split('\n')[0].TrimEnd('\r'));

		string html = await RenderAsync<DockerStatsPanel>(new() { [nameof(DockerStatsPanel.ContainerId)] = "0a1b2c3d4e5f" });

		Assert.Equal(4, Count(html, "class=\"moka-linechart-plot\""));
		Assert.Contains(">Block I/O<", html, StringComparison.Ordinal);
		Assert.Contains("1.23% of 7.6GiB", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Memory in use over the last two minutes", html, StringComparison.Ordinal);
		Assert.DoesNotContain("mt-dk-stat-value", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheInspectTab_SummarisesInTilesAndCards_WithoutTheEnvironment()
	{
		_shell.Answer(Line(DockerCommands.Inspect("f27c9f265ce1aa")), 0, DockerFixtures.Inspect);

		string html = await RenderAsync<DockerContainerPanel>(new()
		{
			[nameof(DockerContainerPanel.ContainerId)] = "f27c9f265ce1aa",
			[nameof(DockerContainerPanel.Tab)] = "inspect",
		});

		Assert.Contains("moka-stat--tile", html, StringComparison.Ordinal);
		Assert.Contains(">Running<", html, StringComparison.Ordinal);
		Assert.Contains(">Unhealthy<", html, StringComparison.Ordinal);
		Assert.Contains("3 failing in a row", html, StringComparison.Ordinal);
		Assert.Contains("policy on-failure", html, StringComparison.Ordinal);
		foreach (string card in (string[])["Runtime", "Image and command", "Last health check", "Ports", "Networks", "Mounts", "Labels"])
		{
			Assert.Contains($">{card}<", html, StringComparison.Ordinal);
		}

		Assert.Contains("/docker-entrypoint-initdb.d", html, StringComparison.Ordinal);
		Assert.Contains("pg_isready: no response", html, StringComparison.Ordinal);
		Assert.Contains("moka-code-block", html, StringComparison.Ordinal);
		Assert.DoesNotContain("moka-code-block__copy", html, StringComparison.Ordinal);
		Assert.DoesNotContain("s3cr3t-demo", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheEnvTab_KeepsEveryValueHidden()
	{
		_shell.Answer(Line(DockerCommands.Inspect("f27c9f265ce1aa")), 0, DockerFixtures.Inspect);

		string html = await RenderAsync<DockerContainerPanel>(new()
		{
			[nameof(DockerContainerPanel.ContainerId)] = "f27c9f265ce1aa",
			[nameof(DockerContainerPanel.Tab)] = "env",
		});

		Assert.Contains("POSTGRES_PASSWORD", html, StringComparison.Ordinal);
		Assert.Contains(DockerInspectParser.Mask, System.Net.WebUtility.HtmlDecode(html), StringComparison.Ordinal);
		Assert.DoesNotContain("s3cr3t-demo", html, StringComparison.Ordinal);
		Assert.Contains("mt-dk-variables", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheOverview_DrawsTheDiskUsageAsATable()
	{
		string html = await RenderAsync<DockerOverviewPage>();

		Assert.Contains("aria-label=\"Disk usage\"", html, StringComparison.Ordinal);
		Assert.Contains(">Build Cache<", html, StringComparison.Ordinal);
		Assert.Contains("282.3MB (13%)", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Disk space docker uses, by kind\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheContainers_ShowDockersStatus_WithoutTheHealthTheirColumnShows()
	{
		string html = await RenderAsync<DockerContainersPage>();

		// The state is a tag, the health while a check runs, with docker's own time beside it.
		Assert.Contains(">Healthy<", html, StringComparison.Ordinal);
		Assert.Contains(">Unhealthy<", html, StringComparison.Ordinal);
		Assert.Contains(">Paused<", html, StringComparison.Ordinal);
		Assert.Contains(">Up 2 days<", html, StringComparison.Ordinal);
		Assert.Contains(">Exited (137)<", html, StringComparison.Ordinal);
		Assert.Contains(">3 hours ago<", html, StringComparison.Ordinal);
		Assert.DoesNotContain("HEALTHY", html, StringComparison.Ordinal);

		// Published ports are tags, once each though docker lists the IPv4 and IPv6 bindings; exposed ones stay text.
		Assert.Equal(1, Count(System.Net.WebUtility.HtmlDecode(html), ">8080→80/tcp<"));
		Assert.Contains(">6379/tcp<", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheCleanupPage_OpensOnTheFirstKindWithSomethingToGo_AndListsExactlyThat()
	{
		string html = await RenderAsync<DockerCleanupPage>();

		Assert.Contains("aria-label=\"Stopped containers\"", html, StringComparison.Ordinal);
		Assert.Contains(">redis<", html, StringComparison.Ordinal);
		Assert.Contains(">seed<", html, StringComparison.Ordinal);
		Assert.DoesNotContain(">web<", html, StringComparison.Ordinal);
		Assert.Contains("Remove 2 containers", html, StringComparison.Ordinal);
	}

	private static string Line(DockerCall call) => DockerCommandLine.Build(call);

	private static int Count(string html, string text)
	{
		int count = 0;
		for (int at = html.IndexOf(text, StringComparison.Ordinal); at >= 0; at = html.IndexOf(text, at + text.Length, StringComparison.Ordinal))
		{
			count++;
		}

		return count;
	}

	private Task<string> RenderAsync<TComponent>(Dictionary<string, object?>? parameters = null)
		where TComponent : IComponent =>
		StaticRender.RenderAsync<TComponent>(
			services =>
			{
				services.AddSingleton<TimeProvider>(_time);
				services.AddMokaRed();
				services.AddMokatermUiCommon();
				services.AddSingleton<IUserInteraction, DismissingInteraction>();
				services.AddSingleton<ISettingsService>(new FakeSettingsService());
				services.AddSingleton<IAppEnvironment>(new FakeAppEnvironment(Path.Combine(Path.GetTempPath(), "mokaterm-unused")));
				services.AddSingleton(UnusedService.Of<ISessionManager>());
				services.AddSingleton(UnusedService.Of<ISessionTabs>());
				services.AddSingleton(UnusedService.Of<IFileBrowserDialog>());
			},
			new Dictionary<string, object?>(parameters ?? new()) { [nameof(DockerContainersPage.Session)] = _session })
			.WaitAsync(TimeSpan.FromSeconds(10), Ct);
}
