using Microsoft.Extensions.Time.Testing;
using Mokaterm.Abstractions.Containers;
using Mokaterm.Abstractions.Security;
using Mokaterm.DevHost.Demo.Docker;
using Mokaterm.DevHost.Demo.FileSystem;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>
/// The DevHost's demo docker played end to end through the module's own shell, client and readers, the way the manager
/// drives it: the client config, a login that reads its password from stdin and refuses a wrong one, logout, a build
/// whose lines arrive over time and that can be stopped, a tag, and a push that a registry refuses until it is logged in to.
/// </summary>
public sealed class DockerDemoRegistryTests
{
	private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(250);

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ClientConfig_ListsTheDemosRegistries()
	{
		(FakeTimeProvider time, DockerSessionFeature feature) = Demo();
		await using (feature)
		{
			DockerClientConfig config = await DriveAsync(time, feature.ReadClientConfigAsync(Ct));

			Assert.Equal("demo", config.User);
			Assert.Equal("/home/demo/.docker/config.json", config.Path);
			Assert.True(config.Find("ghcr.io") is { LoggedIn: true, StoredInFile: true });
			Assert.Equal("ecr-login", config.Find("123456789012.dkr.ecr.eu-west-1.amazonaws.com")!.Helper);
			Assert.Null(config.Find("docker.io"));
		}
	}

	[Fact]
	public async Task Login_ReadsThePasswordFromStdin_AndRefusesAWrongOne()
	{
		(FakeTimeProvider time, DockerSessionFeature feature) = Demo();
		await using (feature)
		{
			using SecretBuffer wrong = SecretBuffer.FromString("not-it");
			using SecretBuffer right = SecretBuffer.FromString(DemoDockerEngine.RegistryPassword);

			DockerOutput refused = await DriveAsync(time, feature.Client.RunAsync(DockerRegistryCommands.Login(null, "alice", wrong), Ct));
			DockerOutput accepted = await DriveAsync(time, feature.Client.RunAsync(DockerRegistryCommands.Login(null, "alice", right), Ct));
			DockerClientConfig config = await DriveAsync(time, feature.ReadClientConfigAsync(Ct));

			Assert.Equal(1, refused.ExitCode);
			Assert.Contains("unauthorized: incorrect username or password", refused.Error, StringComparison.Ordinal);
			Assert.True(accepted.Succeeded, accepted.Error);
			Assert.Contains("Login Succeeded", accepted.Output, StringComparison.Ordinal);
			Assert.StartsWith("WARNING! Your credentials are stored unencrypted", DockerRegistryCommands.StorageWarning(accepted), StringComparison.Ordinal);
			Assert.True(config.Find("docker.io") is { LoggedIn: true, Key: DockerNames.DockerHubRegistry });
		}
	}

	[Fact]
	public async Task Logout_ForgetsTheRegistry()
	{
		(FakeTimeProvider time, DockerSessionFeature feature) = Demo();
		await using (feature)
		{
			DockerOutput output = await DriveAsync(time, feature.Client.RunAsync(DockerRegistryCommands.Logout("ghcr.io"), Ct));
			DockerClientConfig config = await DriveAsync(time, feature.ReadClientConfigAsync(Ct));

			Assert.True(output.Succeeded);
			Assert.Equal("Removing login credentials for ghcr.io", output.Output.Trim());
			Assert.Null(config.Find("ghcr.io"));
		}
	}

	[Fact]
	public async Task Build_PrintsAsItGoes_AndLeavesTheImage()
	{
		(FakeTimeProvider time, DockerSessionFeature feature) = Demo();
		await using (feature)
		{
			DockerBuilder builder = await DriveAsync(time, feature.BuilderAsync(Ct));
			DockerBuildSpec spec = new() { Context = "/srv/shop/api", Tags = ["shop-api:dev"] };

			DockerLiveRun run = feature.StartLive(LivePlan(spec.ToCall(builder)), feature.Client);
			await Eventually.AdvanceUntilAsync(time, Step, () => run.Lines.Snapshot().Count > 3 && run.IsRunning, "the first lines arrive while it runs", maxSteps: 200);
			await Eventually.AdvanceUntilAsync(time, Step, () => run.Completion.IsCompleted, "the build ends", maxSteps: 400);
			IReadOnlyList<DockerImage> images = await DriveAsync(time, feature.Client.ListImagesAsync(Ct));

			Assert.Equal(DockerBuilder.BuildKit, builder);
			Assert.Equal(DockerLiveState.Succeeded, run.State);
			Assert.Contains(run.Lines.Snapshot(), line => line.Text == "#10 naming to docker.io/library/shop-api:dev done");
			Assert.DoesNotContain(run.Lines.Snapshot(), line => line.IsError);
			Assert.Contains(images, image => image.Reference == "shop-api:dev");
		}
	}

	[Fact]
	public async Task Build_Stopped_LeavesNoImage()
	{
		(FakeTimeProvider time, DockerSessionFeature feature) = Demo();
		await using (feature)
		{
			DockerBuildSpec spec = new() { Context = "/srv/shop/api", Tags = ["shop-api:stopped"] };

			DockerLiveRun run = feature.StartLive(LivePlan(spec.ToCall(DockerBuilder.BuildKit)), feature.Client);
			await Eventually.AdvanceUntilAsync(time, Step, () => run.Lines.Snapshot().Count > 3, "the first lines arrive", maxSteps: 200);
			run.Cancel();
			await run.Completion.WaitAsync(TimeSpan.FromSeconds(15), Ct);
			IReadOnlyList<DockerImage> images = await DriveAsync(time, feature.Client.ListImagesAsync(Ct));

			Assert.Equal(DockerLiveState.Stopped, run.State);
			Assert.DoesNotContain(images, image => image.Reference == "shop-api:stopped");
		}
	}

	[Fact]
	public async Task Build_WithoutADockerfile_FailsAsBuildKitDoes()
	{
		(FakeTimeProvider time, DockerSessionFeature feature) = Demo();
		await using (feature)
		{
			DockerLiveRun run = feature.StartLive(LivePlan(new DockerBuildSpec { Context = "/srv/shop" }.ToCall(DockerBuilder.BuildKit)), feature.Client);
			await Eventually.AdvanceUntilAsync(time, Step, () => run.Completion.IsCompleted, "the build ends", maxSteps: 200);

			Assert.Equal(DockerLiveState.Failed, run.State);
			Assert.Equal("ERROR: failed to solve: failed to read dockerfile: open Dockerfile: no such file or directory", run.Problem);
		}
	}

	[Fact]
	public async Task Push_IsRefusedUntilTheRegistryIsLoggedInTo()
	{
		(FakeTimeProvider time, DockerSessionFeature feature) = Demo();
		await using (feature)
		{
			DockerOutput tagged = await DriveAsync(time, feature.Client.RunAsync(DockerCommands.Tag("alpine:3.20", "registry.example.com/ops/alpine:3.20"), Ct));
			DockerLiveRun refused = feature.StartLive(LivePlan(DockerCommands.Push("registry.example.com/ops/alpine:3.20", allTags: false), progress: false), feature.Client);
			await Eventually.AdvanceUntilAsync(time, Step, () => refused.Completion.IsCompleted, "the first push ends", maxSteps: 200);

			using SecretBuffer password = SecretBuffer.FromString(DemoDockerEngine.RegistryPassword);
			DockerOutput login = await DriveAsync(time, feature.Client.RunAsync(DockerRegistryCommands.Login("registry.example.com", "ops", password), Ct));
			DockerLiveRun pushed = feature.StartLive(LivePlan(DockerCommands.Push("registry.example.com/ops/alpine:3.20", allTags: false), progress: false), feature.Client);
			await Eventually.AdvanceUntilAsync(time, Step, () => pushed.Completion.IsCompleted, "the second push ends", maxSteps: 200);

			Assert.True(tagged.Succeeded, tagged.Error);
			Assert.Equal(DockerLiveState.Failed, refused.State);
			Assert.StartsWith("unauthorized: authentication required", refused.Problem, StringComparison.Ordinal);
			Assert.True(login.Succeeded, login.Error);
			Assert.Equal(DockerLiveState.Succeeded, pushed.State);
			Assert.Contains(pushed.Lines.Snapshot(), line => line.Text.StartsWith("3.20: digest: sha256:", StringComparison.Ordinal));
		}
	}

	private static DockerLivePlan LivePlan(DockerCall call, bool progress = true) =>
		new([call], "Demo run", "Runs it.", "Run", DockerLivePlan.ImagesOwner, progress);

	private static (FakeTimeProvider Time, DockerSessionFeature Feature) Demo()
	{
		FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero));
		DemoNode root = DemoNode.NewDirectory("", "root", "root", DemoModes.Octal("755"), time.GetUtcNow().AddDays(-100));
		DemoDockerFiles.Add(root, time.GetUtcNow());
		DemoFileTree tree = new(root, ["root", "demo"], ["root", "docker"], time);
		DemoDockerEngine engine = new(time, DemoDockerFiles.Reader(tree), DemoDockerFiles.ByteReader(tree));
		return (time, DockerSessionFeature.ForCommands(new DemoDockerCommands(engine, time), time, "demo-docker", "demo"));
	}

	/// <summary>Moves fake time on until <paramref name="task"/> is done: every demo command pauses a little first.</summary>
	private static async Task<T> DriveAsync<T>(FakeTimeProvider time, Task<T> task)
	{
		await Eventually.AdvanceUntilAsync(time, Step, () => task.IsCompleted, "the demo answers", maxSteps: 100);
		return await task;
	}
}
