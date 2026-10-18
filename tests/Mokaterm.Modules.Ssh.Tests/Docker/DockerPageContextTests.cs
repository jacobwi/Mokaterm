using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Modules.Ssh.Components.Docker;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Tests.Fakes;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.ToolPages;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>
/// What a Docker manager page from another module gets: its commands become the manager's own calls, with compose the
/// one the manager picked and the manager's limits, run through the session's client, shown with sudo when it runs that
/// way, and noted for the health watch while they change something. The built-in pages are descriptors too.
/// </summary>
public sealed class DockerPageContextTests
{
	[Fact]
	public void ADockerCommand_IsADockerCall_WithAListingsLimits()
	{
		DockerCall call = DockerPageCalls.ToCall(DockerCommand.Docker("ps", "--all", "--format", "{{json .}}"), compose: null);

		Assert.Equal(DockerProgram.Docker, call.Program);
		Assert.Equal(["ps", "--all", "--format", "{{json .}}"], call.Arguments);
		Assert.Equal(DockerCall.DefaultTimeout, call.Timeout);
		Assert.Equal(DockerCall.ListingOutputBytes, call.OutputLimitBytes);
		Assert.Equal("docker ps --all --format '{{json .}}'", call.Display);
	}

	[Fact]
	public void AChange_GetsAChangesWait_AndAskedForTimesAreCapped()
	{
		DockerCall change = DockerPageCalls.ToCall(DockerCommand.Docker("restart", "web").AsChange(), compose: null);
		DockerCall pull = DockerPageCalls.ToCall(DockerCommand.Docker("pull", "nginx").AsChange(TimeSpan.FromHours(3)), compose: null);
		DockerCall quick = DockerPageCalls.ToCall(DockerCommand.Docker("ps") with { Timeout = TimeSpan.Zero }, compose: null);

		Assert.Equal(DockerCall.ChangeTimeout, change.Timeout);
		Assert.Equal(DockerCall.ChangeOutputBytes, change.OutputLimitBytes);
		Assert.Equal(DockerCall.LongTimeout, pull.Timeout);
		Assert.Equal(TimeSpan.FromSeconds(1), quick.Timeout);
	}

	[Fact]
	public void Compose_IsTheOneTheManagerPicked_AndRefusedWhereThereIsNone()
	{
		DockerCommand ls = DockerCommand.Compose("ls", "--format", "json");

		Assert.Equal(DockerProgram.ComposePlugin, DockerPageCalls.ToCall(ls, new ComposeTool(DockerProgram.ComposePlugin, "2.29.1")).Program);
		Assert.Equal("docker-compose ls --format json", DockerPageCalls.ToCall(ls, new ComposeTool(DockerProgram.ComposeStandalone, "2.20.0")).Display);
		Assert.Throws<ArgumentException>(() => DockerPageCalls.ToCall(ls, compose: null));
	}

	[Fact]
	public void TheFolder_IsAbsolute_AndNoWordCarriesNul()
	{
		DockerCall inFolder = DockerPageCalls.ToCall(DockerCommand.Compose("ps") with { WorkingDirectory = "/srv/shop" }, new ComposeTool(DockerProgram.ComposePlugin, "2.29.1"));

		Assert.Equal("cd -- /srv/shop && docker compose ps", inFolder.Display);
		Assert.Throws<ArgumentException>(() => DockerPageCalls.ToCall(DockerCommand.Docker("ps") with { WorkingDirectory = "srv/shop" }, compose: null));
		Assert.Throws<ArgumentException>(() => DockerPageCalls.ToCall(DockerCommand.Docker("inspect", "web\0x"), compose: null));
	}

	[Fact]
	public async Task Commands_RunThroughTheSessionsClient_ShownAsTheyRun()
	{
		FakeDockerShell shell = new FakeDockerShell { IsElevated = true }.Answer("docker volume ls --quiet", 0, "data\n");
		await using DockerSessionFeature feature = Feature(shell);
		List<string> shown = [];
		DockerPageContext context = new(shown.Add, () => feature);

		string display = context.Display(DockerCommand.Docker("volume", "ls", "--quiet"));
		DockerOutput output = await context.RunAsync(DockerCommand.Docker("volume", "ls", "--quiet"), TestContext.Current.CancellationToken);
		string read = await context.ReadAsync(DockerCommand.Docker("volume", "ls", "--quiet"), TestContext.Current.CancellationToken);

		Assert.Equal("sudo docker volume ls --quiet", display);
		Assert.True(context.IsElevated);
		Assert.Equal("web1", context.Host);
		Assert.True(output.Succeeded);
		Assert.Equal("data\n", read);
		Assert.Equal(["docker volume ls --quiet", "docker volume ls --quiet"], shell.Asked);
	}

	[Fact]
	public async Task AReadThatFails_SaysWhyInWords()
	{
		FakeDockerShell shell = new();
		await using DockerSessionFeature feature = Feature(shell);
		DockerPageContext context = new(_ => { }, () => feature);

		DockerOperationException failure = await Assert.ThrowsAnyAsync<DockerOperationException>(
			() => context.ReadAsync(DockerCommand.Docker("stack", "ls"), TestContext.Current.CancellationToken));

		Assert.NotEmpty(failure.Message);
	}

	[Fact]
	public void WithoutASession_NothingRuns()
	{
		DockerPageContext context = new(_ => { }, () => null);

		Assert.Equal("", context.Host);
		Assert.False(context.IsElevated);
		Assert.Null(context.Compose);
		Assert.ThrowsAny<DockerOperationException>(() => context.Display(DockerCommand.Docker("ps")));
	}

	[Fact]
	public void Navigation_IsByPageId_AndANewContainerRequestWaitsForItsPageOnce()
	{
		List<string> shown = [];
		DockerPageContext context = new(shown.Add, () => null);

		((IDockerPageContext)context).Navigate(DockerPageIds.Cleanup);
		context.NewContainer("nginx:1.27");

		Assert.Equal(["cleanup", "containers"], shown);
		Assert.Equal("nginx:1.27", context.TakeNewContainerImage());
		Assert.Null(context.TakeNewContainerImage());
	}

	[Fact]
	public void BuildImage_OpensTheImagesPage_AndAsksForTheFormOnce()
	{
		List<string> shown = [];
		DockerPageContext context = new(shown.Add, () => null);

		context.BuildImage();

		Assert.Equal(["images"], shown);
		Assert.True(context.TakeBuildImage());
		Assert.False(context.TakeBuildImage());
	}

	[Fact]
	public void TheBuiltInPages_AreDescriptors_InTheirOrder()
	{
		ServiceCollection collection = new();
		collection.AddSingleton<ISettingsService>(new FakeSettingsService());
		new SshModule().ConfigureServices(collection);
		using ServiceProvider services = collection.BuildServiceProvider();
		IReadOnlyList<DockerPageDescriptor> pages = services.GetRequiredService<IUiContributions>().DockerPages;

		Assert.Equal(["overview", "containers", "stats", "compose", "images", "volumes", "networks", "cleanup", "registries"], pages.Select(page => page.Id));
		Assert.All(pages, page => Assert.True(typeof(DockerManagerPageBase).IsAssignableFrom(page.Component)));
		Assert.All(pages, page => Assert.Null(page.Unavailable));
	}

	private static DockerSessionFeature Feature(FakeDockerShell shell) =>
		new(shell, authenticate: null, TimeProvider.System, "web1", "alice", files: null);
}
