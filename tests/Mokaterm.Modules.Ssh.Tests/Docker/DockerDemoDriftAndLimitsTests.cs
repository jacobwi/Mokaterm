using System.Text;
using Microsoft.Extensions.Time.Testing;
using Mokaterm.Abstractions.Containers;
using Mokaterm.DevHost.Demo.Docker;
using Mokaterm.DevHost.Demo.FileSystem;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

// The DevHost's demo docker played through the module's own reads: shop's web starts out made from an older
// definition, api rests on a variable nothing sets, the monitoring files do not read, an up makes web again, and
// docker update changes a container's limits the way the daemon would, refusals included.
public sealed class DockerDemoDriftAndLimitsTests
{
	private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(250);

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Shop_HasOneChangedService_OneInDoubt_AndOneCurrent()
	{
		(FakeTimeProvider time, _, DockerSessionFeature feature) = await DemoAsync();
		await using (feature)
		{
			ComposeDrift drift = await ReadAsync(time, feature, "shop");

			Assert.Equal(ComposeDriftState.Changed, drift.State);
			Assert.Equal(ComposeDriftState.Changed, drift.Find("web")!.State);
			Assert.Equal(ComposeDriftState.Unknown, drift.Find("api")!.State);
			Assert.StartsWith("api uses SMTP_PASSWORD", drift.Find("api")!.Detail, StringComparison.Ordinal);
			Assert.Equal(ComposeDriftState.Current, drift.Find("db")!.State);
		}
	}

	[Fact]
	public async Task Up_MakesTheChangedServiceAgain_AndTheMarkGoes()
	{
		(FakeTimeProvider time, _, DockerSessionFeature feature) = await DemoAsync();
		await using (feature)
		{
			_ = await ReadAsync(time, feature, "shop");
			ComposeTarget target = await TargetAsync(time, feature, "shop");

			DockerOutput up = await DriveAsync(time, feature.Client.RunAsync(ComposeCommands.Up(target), Ct));
			ComposeDrift after = Compare(feature, await ProjectAsync(time, feature, "shop"));

			Assert.Contains("Container shop-web-1  Recreated", up.Error, StringComparison.Ordinal);
			Assert.Equal(ComposeDriftState.Current, after.Find("web")!.State);
			Assert.Equal(ComposeDriftState.Unknown, after.State);
		}
	}

	[Fact]
	public async Task AnEditedComposeFile_ChangesTheHashOfWhatItTouched()
	{
		(FakeTimeProvider time, DemoFileTree tree, DockerSessionFeature feature) = await DemoAsync();
		await using (feature)
		{
			string compose = Encoding.UTF8.GetString(tree.Read("/srv/shop/compose.yaml", DemoAccount.Root).Content);
			DemoNode file = tree.BeginWrite("/srv/shop/compose.yaml", overwrite: true, permissions: null, DemoAccount.Root);
			tree.Append(file, Encoding.UTF8.GetBytes(compose.Replace("image: postgres:16", "image: postgres:17", StringComparison.Ordinal)));
			tree.CompleteWrite(file, null);

			ComposeDrift drift = await ReadAsync(time, feature, "shop");

			Assert.Equal(ComposeDriftState.Changed, drift.Find("db")!.State);
		}
	}

	[Fact]
	public async Task Monitoring_WhoseFilesDoNotRead_IsUnknown_WithComposesReason()
	{
		(FakeTimeProvider time, _, DockerSessionFeature feature) = await DemoAsync();
		await using (feature)
		{
			ComposeDrift drift = await ReadAsync(time, feature, "monitoring");

			Assert.Equal(ComposeDriftState.Unknown, drift.State);
			Assert.Equal("docker compose config --hash said: open /opt/monitoring/docker-compose.override.yml: no such file or directory", drift.Problem);
		}
	}

	[Fact]
	public async Task Update_ChangesTheLimits_AndTheDaemonsRefusalsComeThrough()
	{
		(FakeTimeProvider time, _, DockerSessionFeature feature) = await DemoAsync();
		await using (feature)
		{
			DockerContainerLimits before = (await DriveAsync(time, feature.Client.InspectAsync("shop-db-1", Ct))).Limits;
			DockerOutput refused = await DriveAsync(time, feature.Client.RunAsync(new DockerCall { Program = DockerProgram.Docker, Arguments = ["update", "--memory=2g", "shop-db-1"] }, Ct));
			DockerOutput applied = await DriveAsync(time, feature.Client.RunAsync((DockerUpdateSpec.From(before) with { Memory = "2g", MemorySwap = "-1", Cpus = "2", Restart = DockerRestartPolicy.Always }).ToCall("shop-db-1", hostCpus: 4), Ct));
			DockerContainerLimits after = (await DriveAsync(time, feature.Client.InspectAsync("shop-db-1", Ct))).Limits;

			Assert.Equal(1.5m, before.Cpus);
			Assert.Contains("Memory limit should be smaller than already set memoryswap limit", refused.Error, StringComparison.Ordinal);
			Assert.True(applied.Succeeded, applied.Error);
			Assert.Equal(2m, after.Cpus);
			Assert.Equal(2L << 30, after.MemoryBytes);
			Assert.Equal(-1, after.MemorySwapBytes);
			Assert.Equal(DockerRestartPolicy.Always, after.RestartPolicy);
		}
	}

	private static ComposeDrift Compare(DockerSessionFeature feature, ComposeProject project) =>
		ComposeDrift.Compare(feature.ComposeDrift.Find(project.Name), project.Containers, feature.Compose)!;

	private static async Task<ComposeDrift> ReadAsync(FakeTimeProvider time, DockerSessionFeature feature, string name)
	{
		ComposeTarget target = await TargetAsync(time, feature, name);
		_ = await DriveAsync(time, feature.ComposeDrift.ReadAsync(feature.Client, target, Ct));
		return Compare(feature, await ProjectAsync(time, feature, name));
	}

	private static async Task<ComposeTarget> TargetAsync(FakeTimeProvider time, DockerSessionFeature feature, string name) =>
		ComposeTarget.For(feature.Compose!, await ProjectAsync(time, feature, name));

	private static async Task<ComposeProject> ProjectAsync(FakeTimeProvider time, DockerSessionFeature feature, string name)
	{
		IReadOnlyList<DockerContainer> containers = await DriveAsync(time, feature.Client.ListContainersAsync(Ct));
		IReadOnlyList<ComposeProject> projects = await DriveAsync(time, feature.Client.ListComposeProjectsAsync(feature.Compose, containers, Ct));
		return projects.Single(project => project.Name == name);
	}

	// Detection runs first, as the manager's does: it is what finds the compose every compose command is built from.
	private static async Task<(FakeTimeProvider Time, DemoFileTree Tree, DockerSessionFeature Feature)> DemoAsync()
	{
		FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.Zero));
		DemoNode root = DemoNode.NewDirectory("", "root", "root", DemoModes.Octal("755"), time.GetUtcNow().AddDays(-100));
		DemoDockerFiles.Add(root, time.GetUtcNow());
		DemoFileTree tree = new(root, ["root", "demo"], ["root", "docker", "demo"], time);
		DemoDockerEngine engine = new(time, DemoDockerFiles.Reader(tree), DemoDockerFiles.ByteReader(tree), tree, DemoAccount.ForUser("demo"));
		DockerSessionFeature feature = DockerSessionFeature.ForCommands(new DemoDockerCommands(engine, time), time, "demo-docker", "demo");
		_ = await DriveAsync(time, feature.DetectAsync(Ct));
		return (time, tree, feature);
	}

	private static async Task<T> DriveAsync<T>(FakeTimeProvider time, Task<T> task)
	{
		await Eventually.AdvanceUntilAsync(time, Step, () => task.IsCompleted, "the demo answers", maxSteps: 100);
		return await task;
	}
}
