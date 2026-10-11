using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Tests.Fakes;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>Finding docker and compose on a server described by a table of answers.</summary>
public sealed class DockerDetectorTests
{
	private const string VersionLine = "docker version --format '{{json .}}'";

	private const string InfoLine = "docker info --format '{{json .}}'";

	private const string PluginLine = "docker compose version --short";

	private const string StandaloneLine = "docker-compose version --short";

	private const string StandaloneLongLine = "docker-compose -v";

	private const string Version = """
		{"Client":{"Version":"27.3.1","ApiVersion":"1.47","Context":"default","Os":"linux","Arch":"amd64"},"Server":{"Platform":{"Name":"Docker Engine - Community"},"Version":"27.3.1","ApiVersion":"1.47","Os":"linux","Arch":"amd64","KernelVersion":"6.8.0-45-generic"}}
		""";

	private const string ClientOnly = """
		{"Client":{"Version":"27.3.1","ApiVersion":"1.47","Context":"default"},"Server":null}
		""";

	private const string Info = """
		{"Containers":6,"ContainersRunning":3,"ContainersPaused":1,"ContainersStopped":2,"Images":8,"Driver":"overlay2","DockerRootDir":"/var/lib/docker","OperatingSystem":"Ubuntu 24.04.1 LTS","KernelVersion":"6.8.0-45-generic","Architecture":"x86_64","NCPU":4,"MemTotal":8203223040,"Name":"build","SecurityOptions":["name=apparmor","name=seccomp,profile=builtin","name=rootless","name=cgroupns"]}
		""";

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Ready_ReadsTheEngine_AndBothComposes()
	{
		FakeDockerShell shell = new FakeDockerShell()
			.Answer(VersionLine, 0, Version)
			.Answer(InfoLine, 0, Info)
			.Answer(PluginLine, 0, "2.29.7\n")
			.Answer(StandaloneLine, 0, "v2.20.2\n");

		DockerEngineStatus status = await DockerDetector.DetectAsync(shell.Client(), Ct);

		Assert.Equal(DockerAvailability.Ready, status.Availability);
		Assert.Equal("27.3.1", status.Version!.ServerVersion);
		Assert.Equal("1.47", status.Version.ServerApiVersion);
		Assert.True(status.Info!.Rootless);
		Assert.Equal(3, status.Info.Running);
		Assert.Equal(new ComposeTool(DockerProgram.ComposePlugin, "2.29.7"), status.Compose.Plugin);
		Assert.Equal(new ComposeTool(DockerProgram.ComposeStandalone, "2.20.2"), status.Compose.Standalone);
		Assert.DoesNotContain(StandaloneLongLine, shell.Asked);
	}

	[Fact]
	public async Task PermissionDenied_OnTheSocket_IsItsOwnAnswer()
	{
		FakeDockerShell shell = new FakeDockerShell().Answer(
			VersionLine,
			1,
			ClientOnly,
			"permission denied while trying to connect to the Docker daemon socket at unix:///var/run/docker.sock: Get \"http://%2Fvar%2Frun%2Fdocker.sock/v1.47/version\": dial unix /var/run/docker.sock: connect: permission denied");

		DockerEngineStatus status = await DockerDetector.DetectAsync(shell.Client(), Ct);

		Assert.Equal(DockerAvailability.PermissionDenied, status.Availability);
		Assert.Contains("build-server", status.Message, StringComparison.Ordinal);
		Assert.DoesNotContain(InfoLine, shell.Asked);
	}

	[Theory]
	[InlineData("Cannot connect to the Docker daemon at unix:///var/run/docker.sock. Is the docker daemon running?")]
	[InlineData("failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine; check if the path is correct and if the daemon is running")]
	[InlineData("")]
	public async Task NoDaemon_IsDaemonUnreachable(string error)
	{
		FakeDockerShell shell = new FakeDockerShell().Answer(VersionLine, 1, ClientOnly, error);

		DockerEngineStatus status = await DockerDetector.DetectAsync(shell.Client(), Ct);

		Assert.Equal(DockerAvailability.DaemonUnreachable, status.Availability);
		Assert.Equal("27.3.1", status.Version!.ClientVersion);
	}

	[Fact]
	public async Task NoDockerAtAll_IsNotInstalled()
	{
		DockerEngineStatus status = await DockerDetector.DetectAsync(new FakeDockerShell().Client(), Ct);

		Assert.Equal(DockerAvailability.NotInstalled, status.Availability);
	}

	[Fact]
	public async Task Compose_PluginOnly()
	{
		FakeDockerShell shell = new FakeDockerShell().Answer(PluginLine, 0, "2.29.7\n");

		ComposeDetection compose = await DockerDetector.FindComposeAsync(shell.Client(), Ct);

		Assert.Equal("2.29.7", compose.Plugin?.Version);
		Assert.Null(compose.Standalone);
		Assert.Equal(compose.Plugin, compose.Pick(ComposePreference.Auto));
		Assert.Null(compose.Pick(ComposePreference.Standalone));

		// docker-compose was not found at all (exit 127), so it is not asked again the long way.
		Assert.DoesNotContain(StandaloneLongLine, shell.Asked);
	}

	[Fact]
	public async Task Compose_ThePythonOneOnly_AnswersOnlyTheLongWay()
	{
		FakeDockerShell shell = new FakeDockerShell()
			.Answer(PluginLine, 1, "", "docker: 'compose' is not a docker command.\nSee 'docker --help'")
			.Answer(StandaloneLine, 1, "", "No such command: version --short")
			.Answer(StandaloneLongLine, 0, "docker-compose version 1.29.2, build 5becea4c\n");

		ComposeDetection compose = await DockerDetector.FindComposeAsync(shell.Client(), Ct);

		Assert.Null(compose.Plugin);
		ComposeTool legacy = Assert.IsType<ComposeTool>(compose.Standalone);
		Assert.Equal("1.29.2", legacy.Version);
		Assert.True(legacy.IsLegacy);
		Assert.False(legacy.SupportsList);
		Assert.False(legacy.SupportsJson);
		Assert.Equal(legacy, compose.Pick(ComposePreference.Auto));
		Assert.Null(compose.Pick(ComposePreference.Plugin));
	}

	[Fact]
	public async Task Compose_StandaloneVersionTwo_IsNotLegacy()
	{
		FakeDockerShell shell = new FakeDockerShell().Answer(StandaloneLine, 0, "2.20.2\n");

		ComposeDetection compose = await DockerDetector.FindComposeAsync(shell.Client(), Ct);

		ComposeTool tool = Assert.IsType<ComposeTool>(compose.Standalone);
		Assert.False(tool.IsLegacy);
		Assert.True(tool.SupportsList);
		Assert.Equal("docker-compose", tool.Command);
	}

	[Fact]
	public async Task Compose_Both_PreferThePlugin_UnlessTheSessionSaysOtherwise()
	{
		FakeDockerShell shell = new FakeDockerShell()
			.Answer(PluginLine, 0, "v2.29.7\n")
			.Answer(StandaloneLine, 0, "2.20.2\n");

		ComposeDetection compose = await DockerDetector.FindComposeAsync(shell.Client(), Ct);

		Assert.Equal(DockerProgram.ComposePlugin, compose.Pick(ComposePreference.Auto)?.Program);
		Assert.Equal(DockerProgram.ComposePlugin, compose.Pick(ComposePreference.Plugin)?.Program);
		Assert.Equal(DockerProgram.ComposeStandalone, compose.Pick(ComposePreference.Standalone)?.Program);
	}

	[Fact]
	public async Task Compose_Neither()
	{
		ComposeDetection compose = await DockerDetector.FindComposeAsync(new FakeDockerShell().Client(), Ct);

		Assert.False(compose.HasAny);
		Assert.Null(compose.Pick(ComposePreference.Auto));
	}

	[Theory]
	[InlineData("2.29.7", "2.29.7")]
	[InlineData("v2.29.7\n", "2.29.7")]
	[InlineData("Docker Compose version v2.29.7", "2.29.7")]
	[InlineData("docker-compose version 1.29.2, build 5becea4c", "1.29.2")]
	[InlineData("5.5.1", "5.5.1")]
	[InlineData("2.24.0-desktop.1", "2.24.0-desktop.1")]
	[InlineData("", null)]
	[InlineData("command not found", null)]
	public void ParseVersion_TakesEveryWayAComposeSaysIt(string output, string? expected) =>
		Assert.Equal(expected, ComposeTool.ParseVersion(output));

	[Theory]
	[InlineData(true, "2.29.7", "docker compose 2.29.7", false)]
	[InlineData(true, "5.5.1", "docker compose 5.5.1", false)]
	[InlineData(false, "1.29.2", "docker-compose 1.29.2", true)]
	[InlineData(false, "2.20.2", "docker-compose 2.20.2", false)]
	public void ComposeTool_SaysWhatItIs(bool plugin, string version, string display, bool legacy)
	{
		ComposeTool tool = new(plugin ? DockerProgram.ComposePlugin : DockerProgram.ComposeStandalone, version);

		Assert.Equal(display, tool.Display);
		Assert.Equal(legacy, tool.IsLegacy);
		Assert.Equal(!legacy, tool.MaySupportConfigJson);
	}
}
