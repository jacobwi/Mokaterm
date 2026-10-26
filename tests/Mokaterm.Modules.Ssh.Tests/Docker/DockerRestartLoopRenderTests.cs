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

namespace Mokaterm.Modules.Ssh.Tests.Docker;

// The chip, the filter, the overview's count and the panel, rendered for real over a docker answered from a table.
public sealed class DockerRestartLoopRenderTests
{
	private const string PrometheusId = "4f3e2d1c0b0a99887766554433221100ffeeddccbbaa99887766554433221100";
	private const string WebId = "0a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f9";

	private const string Containers = $$"""
		{"Command":"\"/bin/prometheus\"","CreatedAt":"2026-10-05 07:00:00 +0000 UTC","ID":"{{PrometheusId}}","Image":"prom/prometheus:v2.54.1","Labels":"com.docker.compose.project=monitoring,com.docker.compose.service=prometheus,com.docker.compose.project.working_dir=/opt/monitoring","Names":"monitoring-prometheus-1","Networks":"bridge","Ports":"","State":"restarting","Status":"Restarting (137) 3 seconds ago"}
		{"Command":"\"nginx\"","CreatedAt":"2026-10-05 07:00:00 +0000 UTC","ID":"{{WebId}}","Image":"nginx:1.27-alpine","Labels":"","Names":"web","Networks":"bridge","Ports":"","State":"running","Status":"Up 2 hours"}
		""";

	private const string Inspect = $$"""
		[{"Id":"{{PrometheusId}}","Created":"2026-10-05T07:00:00Z","Name":"/monitoring-prometheus-1","Image":"sha256:4ca2d821beb2","RestartCount":44,
		  "State":{"Status":"restarting","Pid":0,"ExitCode":137,"Error":"","OOMKilled":true,"StartedAt":"2026-10-20T08:59:51Z","FinishedAt":"2026-10-20T08:59:57Z"},
		  "HostConfig":{"RestartPolicy":{"Name":"always","MaximumRetryCount":0},"NetworkMode":"bridge"},
		  "Config":{"Image":"prom/prometheus:v2.54.1","Labels":{"com.docker.compose.project":"monitoring","com.docker.compose.service":"prometheus","com.docker.compose.project.working_dir":"/opt/monitoring"} } }]
		""";

	private const string Log = """
		2026-10-20T08:59:51.900000000Z level=info msg="Starting Prometheus Server" mode=server
		2026-10-20T08:59:56.100000000Z level=info component=tsdb msg="WAL segment loaded" segment=903 maxSegment=1288
		""";

	private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

	private readonly FakeTimeProvider _time = new(Now);
	private readonly FakeDockerShell _shell = new();
	private readonly DockerSessionFeature _feature;
	private readonly ScriptedSessionHandle _session;

	public DockerRestartLoopRenderTests()
	{
		_feature = new DockerSessionFeature(_shell, authenticate: null, _time, "build-server", "ada", files: null);
		_session = new ScriptedSessionHandle(new FeatureSession(_feature));
		_shell
			.Answer("docker ps --all --no-trunc --format '{{json .}}'", 0, Containers)
			.Answer($"docker container inspect {PrometheusId}", 0, Inspect)
			.Answer($"docker logs --timestamps --tail 200 {PrometheusId}", 0, Log)
			.Answer("docker system df --format '{{json .}}'", 0, "");
	}

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task TheContainersPage_MarksTheLoopingRow_AndOffersItsFilter()
	{
		Loop();

		string html = await RenderAsync<DockerContainersPage>(Page());

		Assert.Equal(1, Occurrences(html, ">RESTART LOOP<"));
		Assert.Contains("Looping 1", html, StringComparison.Ordinal);
		Assert.Contains("It stopped again and Docker is about to restart it (44 times so far). Its last exit was 137, killed when it ran out of memory.", html, StringComparison.Ordinal);
		Assert.True(
			html.IndexOf(">RESTART LOOP<", StringComparison.Ordinal) < html.IndexOf(">web<", StringComparison.Ordinal),
			"the chip is on the prometheus row, which sorts before web");
	}

	[Fact]
	public async Task WithoutALoop_TheListHasNoChipAndNoFilter()
	{
		string html = await RenderAsync<DockerContainersPage>(Page());

		Assert.DoesNotContain("RESTART LOOP", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Looping", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheRestartsPanel_SaysWhyItKeepsRestarting_AndShowsTheLogBeforeTheCrash()
	{
		Loop();
		Dictionary<string, object?> parameters = Page();
		parameters[nameof(DockerRestartsPanel.ContainerId)] = PrometheusId;

		string html = await RenderAsync<DockerRestartsPanel>(parameters);

		Assert.Contains("Why it keeps restarting", html, StringComparison.Ordinal);
		Assert.Contains("44 in all", html, StringComparison.Ordinal);
		Assert.Contains("always: Docker starts it again whenever it stops", html, StringComparison.Ordinal);
		Assert.Contains("137, killed when it ran out of memory", html, StringComparison.Ordinal);
		Assert.Contains("yes: the kernel killed it at its memory limit", html, StringComparison.Ordinal);
		Assert.Contains(">6 s<", html, StringComparison.Ordinal);
		Assert.Contains("Stop restarting", html, StringComparison.Ordinal);
		Assert.Contains("Stop prometheus", html, StringComparison.Ordinal);
		Assert.Contains("Last logs before it stopped", html, StringComparison.Ordinal);
		Assert.Contains("segment=903 maxSegment=1288", html, StringComparison.Ordinal);
		Assert.Contains($"docker logs --timestamps --tail 200 {PrometheusId}", _shell.Asked);
	}

	[Fact]
	public async Task TheOverview_CountsTheContainersThatKeepRestarting()
	{
		Loop();

		string html = await RenderAsync<DockerOverviewPage>(Page());

		Assert.Contains("1 container keeps restarting", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AComposeServiceRow_MarksTheLoop()
	{
		Loop();
		Dictionary<string, object?> parameters = Page();
		parameters[nameof(DockerComposeProjectView.ProjectName)] = "monitoring";

		string html = await RenderAsync<DockerComposeProjectView>(parameters);

		Assert.Equal(1, Occurrences(html, ">RESTART LOOP<"));
	}

	private static int Occurrences(string text, string part)
	{
		int count = 0;
		for (int index = text.IndexOf(part, StringComparison.Ordinal); index >= 0; index = text.IndexOf(part, index + part.Length, StringComparison.Ordinal))
		{
			count++;
		}

		return count;
	}

	private Dictionary<string, object?> Page() => new() { [nameof(DockerContainersPage.Session)] = _session };

	private void Loop() => _feature.RestartLoops.Observe(
		[
			new DockerContainerState
			{
				Id = PrometheusId,
				Name = "monitoring-prometheus-1",
				State = "restarting",
				RestartCount = 44,
				ExitCode = 137,
				OomKilled = true,
				RestartPolicy = "always",
				StartedAt = Now.AddSeconds(-9),
				FinishedAt = Now.AddSeconds(-3),
				ComposeProject = "monitoring",
			},
			new DockerContainerState { Id = WebId, Name = "web", State = "running", RestartPolicy = "always" },
		],
		Now,
		Now,
		_feature.Changes);

	private Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters)
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
			parameters)
			.WaitAsync(TimeSpan.FromSeconds(10), Ct);
}
