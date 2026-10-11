using Mokaterm.Abstractions.Containers;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>Projects from compose ls and from the containers' labels, and one state per service and per project.</summary>
public sealed class ComposeProjectTests
{
	[Fact]
	public void Parse_WithAListing_TakesItsFiles_AndAddsWhatOnlyLabelsName()
	{
		IReadOnlyList<ComposeProject> projects = ComposeProject.Parse(DockerFixtures.ComposeList, DockerContainer.Parse(DockerFixtures.Containers));

		Assert.Equal(["gone", "monitoring", "shop"], projects.Select(project => project.Name));
		ComposeProject shop = projects[2];
		Assert.Equal(["/srv/shop/compose.yaml", "/srv/shop/compose.prod.yaml"], shop.ConfigFiles);
		Assert.Equal("/srv/shop", shop.WorkingDirectory);
		Assert.Equal("running(2)", shop.Status);

		// A one-off container (compose run) is no member of the project.
		Assert.Equal(["shop-web-1", "shop-db-1"], shop.Containers.Select(container => container.Name));
		Assert.Empty(projects[0].Containers);
	}

	[Fact]
	public void Parse_WithoutAListing_FindsProjectsByLabel()
	{
		IReadOnlyList<ComposeProject> projects = ComposeProject.Parse(null, DockerContainer.Parse(DockerFixtures.Containers));

		Assert.Equal(["monitoring", "shop"], projects.Select(project => project.Name));
		ComposeProject monitoring = projects[0];

		// The label holds a path relative to the folder; it comes back absolute.
		Assert.Equal(["/opt/monitoring/docker-compose.yml"], monitoring.ConfigFiles);
		Assert.Equal("/opt/monitoring", monitoring.WorkingDirectory);
		Assert.Equal(["/opt/monitoring/.env.prod"], monitoring.EnvironmentFiles);
		Assert.Equal("paused(1)", monitoring.Status);
		Assert.Equal("running(2)", projects[1].Status);
	}

	[Fact]
	public void Parse_AbsoluteFilesWithoutAFolderLabel_NameTheFolder()
	{
		DockerContainer container = Container("app-web-1", "running", "Up 1 minute", "com.docker.compose.project=app,com.docker.compose.service=web,com.docker.compose.project.config_files=/data/app/compose.yml");

		ComposeProject project = Assert.Single(ComposeProject.Parse(null, [container]));

		Assert.Equal("/data/app", project.WorkingDirectory);
		Assert.Equal(["/data/app/compose.yml"], project.ConfigFiles);
	}

	[Fact]
	public void Parse_RelativeFilesWithNoFolder_AreDropped_RatherThanGuessed()
	{
		DockerContainer container = Container("app-web-1", "running", "Up 1 minute", "com.docker.compose.project=app,com.docker.compose.service=web,com.docker.compose.project.config_files=compose.yml");

		ComposeProject project = Assert.Single(ComposeProject.Parse(null, [container]));

		Assert.Null(project.WorkingDirectory);
		Assert.Empty(project.ConfigFiles);
	}

	[Theory]
	[InlineData("/srv/shop", "compose.yaml", "/srv/shop/compose.yaml")]
	[InlineData("/srv/shop/", "./compose.yaml", "/srv/shop/compose.yaml")]
	[InlineData("/srv/shop", "../common/base.yaml", "/srv/common/base.yaml")]
	[InlineData("/srv/shop", "/etc/compose.yaml", "/etc/compose.yaml")]
	[InlineData("/srv", "../../../x.yaml", "/x.yaml")]
	[InlineData("/srv/shop", "  sub//dir/./c.yml ", "/srv/shop/sub/dir/c.yml")]
	[InlineData(null, "compose.yaml", null)]
	[InlineData("relative", "compose.yaml", null)]
	[InlineData("/srv", "", null)]
	public void Resolve_FoldsDotsAgainstTheFolder(string? folder, string path, string? expected) =>
		Assert.Equal(expected, DockerHostPaths.Resolve(folder, path));

	[Theory]
	[InlineData("/srv/shop/compose.yaml", "/srv/shop", "compose.yaml")]
	[InlineData("/compose.yaml", "/", "compose.yaml")]
	[InlineData("/srv/shop/", "/srv", "shop")]
	public void DirectoryAndName(string path, string directory, string name)
	{
		Assert.Equal(directory, DockerHostPaths.DirectoryOf(path));
		Assert.Equal(name, DockerHostPaths.NameOf(path));
	}

	[Theory]
	[InlineData("/home/ada/My App", "myapp")]
	[InlineData("/srv/shop.v2", "shopv2")]
	[InlineData("/srv/_tmp-x", "tmp-x")]
	public void ProjectNameFor_IsWhatComposeMakesOfTheFolder(string folder, string expected) =>
		Assert.Equal(expected, DockerHostPaths.ProjectNameFor(folder));

	[Fact]
	public void Group_AScaledService_HasEveryContainer_AndRunsWhenAllDo()
	{
		IReadOnlyList<ComposeService> services = ComposeService.Group(ComposeContainer.Parse(DockerFixtures.ComposePsLines));

		ComposeService web = services[0];
		Assert.Equal("web", web.Name);
		Assert.True(web.IsScaled);
		Assert.Equal(2, web.Running);
		Assert.Equal(ComposeServiceState.Running, web.State);
		Assert.Equal(DockerHealth.Healthy, web.Health);
		Assert.Equal("2 days", web.Uptime);
		Assert.Equal(["8080→80/tcp"], web.Ports);
		Assert.Equal("nginx:1.27-alpine", web.Image);
	}

	[Fact]
	public void Group_AServiceThatExitedWithAnError_HasFailed_AndSaysTheCode()
	{
		IReadOnlyList<ComposeService> services = ComposeService.Group(ComposeContainer.Parse(DockerFixtures.ComposePsLines));

		ComposeService worker = services.Single(service => service.Name == "worker.v2");
		Assert.Equal(ComposeServiceState.Failed, worker.State);
		Assert.Equal("exit 1", worker.Problem);
		Assert.Equal(ComposeProjectHealth.Unhealthy, ComposeService.HealthOf(services));
	}

	[Fact]
	public void Group_UnhealthyAndRestarting_AreProblems()
	{
		IReadOnlyList<ComposeService> services = ComposeService.Group(ComposeContainer.Parse(DockerFixtures.ComposePsArray));

		ComposeService db = services.Single(service => service.Name == "db");
		ComposeService api = services.Single(service => service.Name == "api");
		Assert.Equal(ComposeServiceState.Unhealthy, db.State);
		Assert.Equal("unhealthy", db.Problem);
		Assert.Equal(ComposeServiceState.Failed, api.State);
		Assert.Equal("restarting", api.Problem);
	}

	[Fact]
	public void Group_PartlyRunning_IsPartial()
	{
		ComposeContainer[] containers =
		[
			Running("shop-web-1", "web"),
			new() { Name = "shop-web-2", Service = "web", State = "exited", Status = "Exited (0) 2 minutes ago" },
		];

		ComposeService web = Assert.Single(ComposeService.Group(containers));

		Assert.Equal(ComposeServiceState.Partial, web.State);
		Assert.Equal("", web.Problem);
		Assert.Equal(1, web.Running);
		Assert.Equal(ComposeProjectHealth.Partial, ComposeService.HealthOf([web]));
	}

	[Fact]
	public void Group_DefinedServicesWithoutContainers_KeepTheFilesOrder()
	{
		IReadOnlyList<ComposeService> services = ComposeService.Group([Running("shop-db-1", "db")], ["web", "api", "db"]);

		Assert.Equal(["web", "api", "db"], services.Select(service => service.Name));
		Assert.Equal(ComposeServiceState.NoContainer, services[0].State);
		Assert.Equal(ComposeProjectHealth.Partial, ComposeService.HealthOf(services));
	}

	[Fact]
	public void HealthOf_TheProjectRule()
	{
		ComposeService running = new() { Name = "a", State = ComposeServiceState.Running, Containers = [Running("a-1", "a")] };
		ComposeService stopped = new() { Name = "b", State = ComposeServiceState.Stopped };
		ComposeService unhealthy = new() { Name = "c", State = ComposeServiceState.Unhealthy, Containers = [Running("c-1", "c")] };
		ComposeService failed = new() { Name = "d", State = ComposeServiceState.Failed };

		Assert.Equal(ComposeProjectHealth.Empty, ComposeService.HealthOf([]));
		Assert.Equal(ComposeProjectHealth.Running, ComposeService.HealthOf([running, running]));
		Assert.Equal(ComposeProjectHealth.Partial, ComposeService.HealthOf([running, stopped]));
		Assert.Equal(ComposeProjectHealth.Stopped, ComposeService.HealthOf([stopped]));
		Assert.Equal(ComposeProjectHealth.Unhealthy, ComposeService.HealthOf([running, unhealthy]));
		Assert.Equal(ComposeProjectHealth.Unhealthy, ComposeService.HealthOf([stopped, failed]));
	}

	[Fact]
	public void ProjectHealth_ComesFromItsContainers()
	{
		IReadOnlyList<ComposeProject> projects = ComposeProject.Parse(null, DockerContainer.Parse(DockerFixtures.Containers));

		// The shop's db reports unhealthy, which outranks everything running.
		Assert.Equal(ComposeProjectHealth.Unhealthy, projects.Single(project => project.Name == "shop").Health);

		// A paused container is not running, so a project of one paused container is stopped.
		Assert.Equal(ComposeProjectHealth.Stopped, projects.Single(project => project.Name == "monitoring").Health);
	}

	[Theory]
	[InlineData("Up 2 hours (healthy)", "2 hours")]
	[InlineData("Up About a minute", "About a minute")]
	[InlineData("Exited (0) 3 hours ago", "")]
	public void UptimeOf_TheUpLine(string status, string expected) => Assert.Equal(expected, ComposeService.UptimeOf(status));

	[Theory]
	[InlineData("Exited (137) 3 hours ago", 137)]
	[InlineData("Exited (0) 1 second ago", 0)]
	[InlineData("Up 2 hours", null)]
	[InlineData("Restarting (2) 10 seconds ago", null)]
	public void ExitCodeOf_TheExitedLine(string status, int? expected) => Assert.Equal(expected, ComposeService.ExitCodeOf(status));

	private static ComposeContainer Running(string name, string service) =>
		new() { Name = name, Service = service, State = "running", Status = "Up 3 minutes" };

	private static DockerContainer Container(string name, string state, string status, string labels) =>
		DockerContainer.FromRow(new DockerContainerRow { Id = name + "-id", Name = name, State = state, Status = status, Labels = labels });
}
