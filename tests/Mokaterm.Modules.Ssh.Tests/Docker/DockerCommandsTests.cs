using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>Every docker command the manager runs, as the exact line the confirmation shows and the server runs.</summary>
public sealed class DockerCommandsTests
{
	[Fact]
	public void Listings_AskForOneJsonObjectPerLine()
	{
		Assert.Equal("docker version --format '{{json .}}'", DockerCommands.Version().Display);
		Assert.Equal("docker info --format '{{json .}}'", DockerCommands.Info().Display);
		Assert.Equal("docker ps --all --no-trunc --format '{{json .}}'", DockerCommands.ListContainers().Display);
		Assert.Equal("docker image ls --no-trunc --format '{{json .}}'", DockerCommands.ListImages().Display);
		Assert.Equal("docker volume ls --format '{{json .}}'", DockerCommands.ListVolumes().Display);
		Assert.Equal("docker volume ls --quiet --filter dangling=true", DockerCommands.UnusedVolumes().Display);
		Assert.Equal("docker network ls --no-trunc --format '{{json .}}'", DockerCommands.ListNetworks().Display);
		Assert.Equal("docker system df --format '{{json .}}'", DockerCommands.DiskUsage().Display);
	}

	[Fact]
	public void ComposeVersions_AskEachComposeOnItsOwn()
	{
		Assert.Equal("docker compose version --short", DockerCommands.ComposePluginVersion().Display);
		Assert.Equal("docker-compose version --short", DockerCommands.ComposeStandaloneVersion().Display);
		Assert.Equal("docker-compose -v", DockerCommands.ComposeStandaloneVersionLong().Display);
	}

	[Fact]
	public void ContainerActions_NameTheContainerLast()
	{
		Assert.Equal("docker start web", DockerCommands.Start("web").Display);
		Assert.Equal("docker stop web", DockerCommands.Stop("web").Display);
		Assert.Equal("docker restart web", DockerCommands.Restart("web").Display);
		Assert.Equal("docker pause web", DockerCommands.Pause("web").Display);
		Assert.Equal("docker unpause web", DockerCommands.Unpause("web").Display);
		Assert.Equal("docker kill web", DockerCommands.Kill("web").Display);
		Assert.Equal("docker container inspect shop_db.1", DockerCommands.Inspect("shop_db.1").Display);
	}

	[Fact]
	public void Remove_KeepsVolumesUnlessAsked()
	{
		Assert.Equal("docker rm web", DockerCommands.Remove("web", removeVolumes: false, force: false).Display);
		Assert.Equal("docker rm --force --volumes web", DockerCommands.Remove("web", removeVolumes: true, force: true).Display);
	}

	[Fact]
	public void Changes_GetTheLongerTimeAndTheSmallerCap()
	{
		DockerCall stop = DockerCommands.Stop("web");
		DockerCall pull = DockerCommands.Pull("nginx:1.27");

		Assert.Equal(DockerCall.ChangeTimeout, stop.Timeout);
		Assert.Equal(DockerCall.ChangeOutputBytes, stop.OutputLimitBytes);
		Assert.Equal(DockerCall.LongTimeout, pull.Timeout);
		Assert.Equal(DockerCall.DefaultTimeout, DockerCommands.ListContainers().Timeout);
	}

	[Fact]
	public void Logs_TailAndFollowFromTheNewestLineShown()
	{
		Assert.Equal("docker logs --timestamps --tail 500 web", DockerCommands.Logs("web", 500, null).Display);
		Assert.Equal(
			"docker logs --timestamps --tail 10000 --since 2026-10-11T07:00:00.123456789Z web",
			DockerCommands.Logs("web", 10_000, "2026-10-11T07:00:00.123456789Z").Display);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(10_001)]
	public void Logs_ATailOutOfRange_IsRefused(int tail) =>
		Assert.Throws<ArgumentOutOfRangeException>(() => DockerCommands.Logs("web", tail, null));

	[Theory]
	[InlineData("yesterday")]
	[InlineData("2026-10-11T07:00:00Z; rm -rf /")]
	[InlineData("--until")]
	public void Logs_ASinceThatIsNotATimestamp_IsRefused(string since) =>
		Assert.Throws<ArgumentException>(() => DockerCommands.Logs("web", 100, since));

	[Theory]
	[InlineData("-rf")]
	[InlineData("--privileged")]
	[InlineData("my container")]
	[InlineData("web;reboot")]
	[InlineData("x")]
	[InlineData("")]
	[InlineData(" web")]
	public void AContainerDockerWouldNotName_NeverReachesACommandLine(string container)
	{
		ArgumentException failure = Assert.ThrowsAny<ArgumentException>(() => DockerCommands.Stop(container));

		Assert.False(string.IsNullOrWhiteSpace(failure.Message));
	}

	[Theory]
	[InlineData("nginx", "docker pull nginx")]
	[InlineData("nginx:1.27-alpine", "docker pull nginx:1.27-alpine")]
	[InlineData("ghcr.io/acme/shop-api:2.4.1", "docker pull ghcr.io/acme/shop-api:2.4.1")]
	[InlineData("registry.local:5000/team/app@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", "docker pull registry.local:5000/team/app@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
	public void Pull_TakesEveryReferenceForm(string reference, string expected) =>
		Assert.Equal(expected, DockerCommands.Pull(reference).Display);

	[Theory]
	[InlineData("-v")]
	[InlineData("Nginx")]
	[InlineData("nginx latest")]
	[InlineData("nginx:")]
	public void Pull_AReferenceDockerWouldRefuse_IsRefusedHere(string reference) =>
		Assert.ThrowsAny<ArgumentException>(() => DockerCommands.Pull(reference));

	[Fact]
	public void Images_RemoveOneOrExactlyTheOnesNamed()
	{
		Assert.Equal("docker image rm nginx:1.27", DockerCommands.RemoveImage("nginx:1.27", force: false).Display);
		Assert.Equal("docker image rm --force 4ca2d821beb2", DockerCommands.RemoveImage("4ca2d821beb2", force: true).Display);
		Assert.Equal("docker image rm busybox:latest 4ca2d821beb2", DockerCommands.RemoveImages(["busybox:latest", "4ca2d821beb2"]).Display);
		Assert.Throws<ArgumentException>(() => DockerCommands.RemoveImages(["-a"]));
	}

	[Fact]
	public void Cleanup_ReadsEverythingWithItsSize_AndRemovesByName()
	{
		Assert.Equal("docker system df --verbose --format '{{json .}}'", DockerCommands.SystemUsage().Display);
		Assert.Equal("docker rm redis seed", DockerCommands.RemoveContainers(["redis", "seed"]).Display);
		Assert.Equal("docker builder prune --all --force", DockerCommands.PruneBuildCache().Display);
		Assert.Equal(DockerCall.LongTimeout, DockerCommands.PruneBuildCache().Timeout);
		Assert.Throws<ArgumentException>(() => DockerCommands.RemoveContainers(["--all"]));
	}

	[Fact]
	public void UpdateChecks_ReadLocalDigests_ThenAskTheRegistry()
	{
		Assert.Equal("docker image inspect nginx:1.27-alpine redis", DockerCommands.InspectImages(["nginx:1.27-alpine", "redis"]).Display);
		Assert.Equal("docker buildx version", DockerCommands.BuildxVersion().Display);
		Assert.Equal("docker buildx imagetools inspect ghcr.io/acme/shop-api:2.4.1", DockerCommands.ImagetoolsInspect("ghcr.io/acme/shop-api:2.4.1").Display);
		Assert.Equal("docker manifest inspect --verbose nginx:1.27-alpine", DockerCommands.ManifestInspect("nginx:1.27-alpine").Display);
		Assert.Throws<ArgumentException>(() => DockerCommands.ImagetoolsInspect("--insecure"));
	}

	[Fact]
	public void ContainerStates_IsOneScript_WithTheTemplateAsAnArgument()
	{
		DockerCall call = DockerCommands.ContainerStates();

		Assert.Equal(DockerProgram.Shell, call.Program);
		Assert.Equal(DockerCommands.ContainerStatesFormat, call.Arguments[^1]);
		Assert.StartsWith("sh -c 'ids=$(docker ps --all --quiet --no-trunc)", call.Display, StringComparison.Ordinal);
		Assert.DoesNotContain("{{", call.Arguments[0], StringComparison.Ordinal);
	}

	[Fact]
	public void VolumesAndNetworks_CreateAndRemoveExactlyWhatIsNamed()
	{
		Assert.Equal("docker volume create shop_pgdata", DockerCommands.CreateVolume("shop_pgdata").Display);
		Assert.Equal("docker volume rm old-cache 2e96105c29d2", DockerCommands.RemoveVolumes(["old-cache", "2e96105c29d2"]).Display);
		Assert.Equal("docker network create backend", DockerCommands.CreateNetwork("backend").Display);
		Assert.Equal("docker network rm legacy_net", DockerCommands.RemoveNetworks(["legacy_net"]).Display);
		Assert.Equal("docker volume inspect shop_pgdata", DockerCommands.InspectVolume("shop_pgdata").Display);
		Assert.Equal(
			"docker ps --all --no-trunc --filter volume=shop_pgdata --format '{{json .}}'",
			DockerCommands.ContainersUsingVolume("shop_pgdata").Display);
		Assert.Throws<ArgumentOutOfRangeException>(() => DockerCommands.RemoveVolumes([]));
	}

	[Fact]
	public void NewNames_FollowTheRestrictedRule()
	{
		Assert.ThrowsAny<ArgumentException>(() => DockerCommands.CreateVolume("a"));
		Assert.ThrowsAny<ArgumentException>(() => DockerCommands.CreateNetwork("-net"));
		Assert.ThrowsAny<ArgumentException>(() => DockerCommands.CreateVolume("my volume"));
	}

	[Fact]
	public void Stats_OneSample_ForTheNamedContainersOrAll()
	{
		Assert.Equal("docker stats --no-stream --no-trunc --format '{{json .}}'", DockerCommands.Stats([]).Display);
		Assert.Equal("docker stats --no-stream --no-trunc --format '{{json .}}' web db", DockerCommands.Stats(["web", "db"]).Display);
	}

	// exec puts docker in the login shell's place, so leaving the container ends the tab's session.
	[Fact]
	public void ShellStartupCommand_ExecsDocker_AndSudoWhenElevated()
	{
		Assert.Equal("exec docker exec -it shop-api-1 bash", DockerCommands.ShellStartupCommand("shop-api-1", "bash", elevated: false));
		Assert.Equal("exec sudo docker exec -it shop-api-1 sh", DockerCommands.ShellStartupCommand("shop-api-1", "sh", elevated: true));
		Assert.Throws<ArgumentException>(() => DockerCommands.ShellStartupCommand("web", "zsh", elevated: false));
		Assert.ThrowsAny<ArgumentException>(() => DockerCommands.ShellStartupCommand("web\u001b[A", "sh", elevated: false));
		Assert.ThrowsAny<ArgumentException>(() => DockerCommands.ShellStartupCommand("-rm", "sh", elevated: false));
	}

	[Fact]
	public void ToString_NeverPrintsTheLine()
	{
		DockerCall call = new DockerExecRequest { Container = "web", Command = "mysql -psecret-pass" }.ToCall();

		Assert.DoesNotContain("secret-pass", call.ToString(), StringComparison.Ordinal);
		Assert.Contains("secret-pass", call.Display, StringComparison.Ordinal);
	}

	[Fact]
	public void FindShell_AsksTheContainersOwnSh()
	{
		DockerCall call = DockerCommands.FindShell("web");

		Assert.Equal("docker exec web sh -c 'command -v bash'", call.Display);
		Assert.Equal(TimeSpan.FromSeconds(15), call.Timeout);
	}
	[Fact]
	public void ExecScript_PassesPathsAsWordsAfterTheScript_AndKeepsTheBytes()
	{
		DockerCall call = DockerCommands.ExecScript("web-1", "cat -- \"$1\"", ["/srv/it's here.txt"]);

		Assert.Equal(["exec", "web-1", "sh", "-c", "cat -- \"$1\"", "sh", "/srv/it's here.txt"], call.Arguments);
		Assert.Equal("docker exec web-1 sh -c 'cat -- \"$1\"' sh '/srv/it'\\''s here.txt'", call.Display);
		Assert.True(call.BinaryOutput);
	}

	[Fact]
	public void ExecScript_ThroughSudo_KeepsEveryPathAnArgument()
	{
		DockerCall call = DockerCommands.ExecScript("web-1", "cat -- \"$1\"", ["/srv/$(reboot)"]);

		(string script, IReadOnlyList<string> arguments) = DockerCommandLine.ForScript(call);

		Assert.Equal(DockerCommandLine.ElevatedScript, script);
		Assert.Equal(["docker", "exec", "web-1", "sh", "-c", "cat -- \"$1\"", "sh", "/srv/$(reboot)"], arguments);
	}

	[Fact]
	public void ExecScriptWithInput_PipesTheHostFileIntoAnInteractiveExec()
	{
		DockerCall call = DockerCommands.ExecScriptWithInput("web-1", "/tmp/.mokaterm-upload-1", "cat > \"$1\"", ["/srv/a"]);

		Assert.Equal(DockerProgram.Shell, call.Program);
		Assert.Equal(
			[DockerHostScripts.WithInputScript, "sh", "/tmp/.mokaterm-upload-1", "docker", "exec", "-i", "web-1", "sh", "-c", "cat > \"$1\"", "sh", "/srv/a"],
			call.Arguments);
		Assert.True(call.BinaryOutput);
	}

	[Fact]
	public void ExecScripts_RefuseWhatIsNotAContainerOrAHostPath()
	{
		Assert.Throws<ArgumentException>(() => DockerCommands.ExecScript("-u", "true", []));
		Assert.Throws<ArgumentException>(() => DockerCommands.ExecScriptWithInput("web-1", "upload.part", "true", []));
	}
}
