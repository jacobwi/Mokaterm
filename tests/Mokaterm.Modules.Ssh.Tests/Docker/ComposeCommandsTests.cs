using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>Every compose command, built from one target, as the exact line that runs.</summary>
public sealed class ComposeCommandsTests
{
	private static readonly ComposeTool Plugin = new(DockerProgram.ComposePlugin, "2.29.7");

	private static readonly ComposeTool Standalone = new(DockerProgram.ComposeStandalone, "2.20.2");

	private static readonly ComposeTool Legacy = new(DockerProgram.ComposeStandalone, "1.29.2");

	private static readonly ComposeTarget Shop = new(Plugin, "shop", "/srv/shop", ["/srv/shop/compose.yaml"], []);

	private const string Prefix = "cd -- /srv/shop && docker compose -p shop -f /srv/shop/compose.yaml";

	[Fact]
	public void EveryProjectAction_RunsInTheProjectsFolder_WithItsNameAndFiles()
	{
		Assert.Equal($"{Prefix} restart", ComposeCommands.Restart(Shop).Display);
		Assert.Equal($"{Prefix} stop", ComposeCommands.Stop(Shop).Display);
		Assert.Equal($"{Prefix} start", ComposeCommands.Start(Shop).Display);
		Assert.Equal($"{Prefix} pull", ComposeCommands.Pull(Shop).Display);
		Assert.Equal($"{Prefix} up --detach", ComposeCommands.Up(Shop).Display);
		Assert.Equal($"{Prefix} up --detach --build", ComposeCommands.Up(Shop, build: true).Display);
		Assert.Equal($"{Prefix} up --detach --force-recreate", ComposeCommands.Up(Shop, forceRecreate: true).Display);
		Assert.Equal($"{Prefix} down", ComposeCommands.Down(Shop, removeVolumes: false).Display);
		Assert.Equal($"{Prefix} down --volumes", ComposeCommands.Down(Shop, removeVolumes: true).Display);
	}

	[Fact]
	public void EveryServiceAction_NamesTheServiceLast()
	{
		Assert.Equal($"{Prefix} restart api", ComposeCommands.Restart(Shop, "api").Display);
		Assert.Equal($"{Prefix} stop api", ComposeCommands.Stop(Shop, "api").Display);
		Assert.Equal($"{Prefix} start api", ComposeCommands.Start(Shop, "api").Display);
		Assert.Equal($"{Prefix} pull api", ComposeCommands.Pull(Shop, "api").Display);
	}

	[Fact]
	public void Recreate_OneService_LeavesItsDependenciesAlone()
	{
		DockerCall call = ComposeCommands.Recreate(Shop, "api");

		Assert.Equal($"{Prefix} up --detach --force-recreate --no-deps api", call.Display);
		Assert.Equal(DockerCall.LongTimeout, call.Timeout);
	}

	[Fact]
	public void Readings_ListTheProjectAndItsConfig()
	{
		Assert.Equal("docker compose ls --all --format json", ComposeCommands.List(Plugin).Display);
		Assert.Equal($"{Prefix} ps --all --format json", ComposeCommands.Containers(Shop).Display);
		Assert.Equal($"{Prefix} config --services", ComposeCommands.Services(Shop).Display);
		Assert.Equal($"{Prefix} config", ComposeCommands.Config(Shop).Display);
		Assert.Equal($"{Prefix} config --format json", ComposeCommands.ConfigJson(Shop).Display);
		Assert.Equal($"{Prefix} config --quiet", ComposeCommands.ConfigCheck(Shop).Display);
	}

	[Fact]
	public void Logs_AllServicesOrOne_FollowingFromATimestamp()
	{
		Assert.Equal($"{Prefix} logs --no-color --timestamps --tail 100", ComposeCommands.Logs(Shop, null, 100).Display);
		Assert.Equal(
			$"{Prefix} logs --no-color --timestamps --tail 10000 --since 2026-10-11T07:00:00.5Z web",
			ComposeCommands.Logs(Shop, "web", 10_000, "2026-10-11T07:00:00.5Z").Display);
	}

	[Fact]
	public void Logs_ThePythonCompose_HasNoSince()
	{
		ComposeTarget legacy = Shop with { Tool = Legacy };

		Assert.Equal(
			"cd -- /srv/shop && docker-compose -p shop -f /srv/shop/compose.yaml logs --no-color --timestamps --tail 100",
			ComposeCommands.Logs(legacy, null, 100).Display);
		Assert.Throws<ArgumentException>(() => ComposeCommands.Logs(legacy, null, 100, "2026-10-11T07:00:00Z"));
	}

	[Fact]
	public void TheStandaloneCompose_GetsTheSameWords()
	{
		Assert.Equal(
			"cd -- /srv/shop && docker-compose -p shop -f /srv/shop/compose.yaml up --detach --force-recreate --no-deps api",
			ComposeCommands.Recreate(Shop with { Tool = Standalone }, "api").Display);
		Assert.Equal("docker-compose ls --all --format json", ComposeCommands.List(Standalone).Display);
	}

	[Fact]
	public void SeveralFiles_AndEnvFiles_AreAllPassedAgain()
	{
		ComposeTarget target = new(
			Plugin,
			"shop",
			"/srv/shop",
			["/srv/shop/compose.yaml", "/srv/shop/compose.prod.yaml"],
			["/srv/shop/.env.prod"]);

		Assert.Equal(
			"cd -- /srv/shop && docker compose -p shop -f /srv/shop/compose.yaml -f /srv/shop/compose.prod.yaml --env-file /srv/shop/.env.prod ps --all --format json",
			ComposeCommands.Containers(target).Display);
	}

	[Fact]
	public void FilesOutsideTheFolder_TellComposeTheFolderAgain()
	{
		ComposeTarget target = new(Plugin, "shop", "/srv/shop", ["/etc/shop/compose.yaml"], []);

		Assert.Equal(
			"cd -- /srv/shop && docker compose -p shop -f /etc/shop/compose.yaml --project-directory /srv/shop stop",
			ComposeCommands.Stop(target).Display);
	}

	[Fact]
	public void AFoundFile_RunsWithoutAProjectName_InItsFolder()
	{
		ComposeTarget target = ComposeTarget.ForFile(Plugin, "/home/ada/sandbox app/compose.yaml");

		Assert.Null(target.Project);
		Assert.True(target.HasFiles);
		Assert.Equal(
			"cd -- '/home/ada/sandbox app' && docker compose -f '/home/ada/sandbox app/compose.yaml' up --detach",
			ComposeCommands.Up(target).Display);
	}

	[Fact]
	public void AProjectWithoutFilesOrFolder_StillHasItsName()
	{
		ComposeTarget target = new(Plugin, "orphans", null, [], []);

		Assert.False(target.HasFiles);
		Assert.Equal("docker compose -p orphans stop", ComposeCommands.Stop(target).Display);
	}

	[Theory]
	[InlineData("Shop", null, null)]
	[InlineData("-p", null, null)]
	[InlineData("shop", "relative/dir", null)]
	[InlineData("shop", "/srv/shop", "compose.yaml")]
	public void ANameOrPathComposeWouldNotTake_IsRefused(string project, string? folder, string? file)
	{
		ComposeTarget target = new(Plugin, project, folder, file is null ? [] : [file], []);

		Assert.ThrowsAny<ArgumentException>(() => ComposeCommands.Stop(target));
	}

	[Theory]
	[InlineData("-d")]
	[InlineData("api;rm")]
	[InlineData("")]
	public void AServiceComposeWouldNotName_IsRefused(string service) =>
		Assert.ThrowsAny<ArgumentException>(() => ComposeCommands.Restart(Shop, service));

	[Fact]
	public void ServiceNames_TakeDotsAndCapitals()
	{
		Assert.Equal($"{Prefix} restart Api.v2_worker-1", ComposeCommands.Restart(Shop, "Api.v2_worker-1").Display);
	}
}
