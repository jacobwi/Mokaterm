using Microsoft.Extensions.Time.Testing;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Tests.Fakes;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>The session's update checks against a server described by a table of answers: which tool asks, and what is kept.</summary>
public sealed class DockerImageUpdatesTests
{
	private const string Index = "sha256:2140dad235c130ac861018a4e13a6bc8aea3a35f3a40e20c1b060d51a7efd250";
	private const string Redis = "sha256:7777777777777777777777777777777777777777777777777777777777777777";
	private const string Older = "sha256:0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d";

	private const string InspectLine = "docker image inspect nginx:1.27-alpine redis:7.4 shop-worker:dev";

	private const string Inspect = $$"""
		[
			{ "Id": "sha256:aaaa1111aaaa1111aaaa1111aaaa1111aaaa1111aaaa1111aaaa1111aaaa1111", "RepoTags": ["nginx:1.27-alpine"], "RepoDigests": ["nginx@{{Older}}"], "Os": "linux", "Architecture": "amd64" },
			{ "Id": "sha256:bbbb2222bbbb2222bbbb2222bbbb2222bbbb2222bbbb2222bbbb2222bbbb2222", "RepoTags": ["redis:7.4"], "RepoDigests": ["redis@{{Redis}}"], "Os": "linux", "Architecture": "amd64" },
			{ "Id": "sha256:cccc3333cccc3333cccc3333cccc3333cccc3333cccc3333cccc3333cccc3333", "RepoTags": ["shop-worker:dev"], "RepoDigests": [], "Os": "linux", "Architecture": "amd64" }
		]
		""";

	private const string RedisImagetools = $"""
		Name:      docker.io/library/redis:7.4
		MediaType: application/vnd.docker.distribution.manifest.v2+json
		Digest:    {Redis}
		""";

	private const string RedisManifest = $$"""
		{ "Ref": "docker.io/library/redis:7.4", "Descriptor": { "digest": "{{Redis}}", "platform": { "architecture": "amd64", "os": "linux" } } }
		""";

	private const string NginxManifest = $$"""
		{ "Ref": "docker.io/library/nginx:1.27-alpine", "Descriptor": { "digest": "{{Index}}", "platform": { "architecture": "amd64", "os": "linux" } } }
		""";

	private static readonly string[] Wanted = ["nginx:1.27-alpine", "redis:7.4", "shop-worker:dev", "4ca2d821beb2"];

	private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 11, 7, 0, 0, TimeSpan.Zero));

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	[Fact]
	public async Task WithBuildx_AsksEachTag_AndSortsWhatItFound()
	{
		FakeDockerShell shell = new FakeDockerShell()
			.Answer(InspectLine, 0, Inspect)
			.Answer("docker buildx version", 0, "github.com/docker/buildx v0.17.1 257815a")
			.Answer("docker buildx imagetools inspect nginx:1.27-alpine", 0, ImageUpdateCheckTests.ImagetoolsIndex)
			.Answer("docker buildx imagetools inspect redis:7.4", 0, RedisImagetools);
		DockerImageUpdates updates = new(_time);
		List<(int Done, int Total)> progress = [];

		IReadOnlyList<ImageUpdateResult> results = await updates.CheckAsync(shell.Client(), Wanted, force: false, (done, total) => progress.Add((done, total)), Ct);

		Assert.Equal(
			[ImageUpdateState.LocalOnly, ImageUpdateState.NoTag, ImageUpdateState.UpdateAvailable, ImageUpdateState.UpToDate],
			results.Select(result => result.State));
		Assert.True(updates.Find("docker.io/library/nginx:1.27-alpine")!.HasUpdate);
		Assert.Equal(ImageUpdateState.UpToDate, updates.Find("redis:7.4")!.State);
		Assert.Equal((4, 4), progress[^1]);
		Assert.DoesNotContain(shell.Asked, line => line.Contains("shop-worker", StringComparison.Ordinal) && line.Contains("imagetools", StringComparison.Ordinal));
	}

	[Fact]
	public async Task AFreshAnswer_IsNotAskedAgain_UnlessForced()
	{
		FakeDockerShell shell = new FakeDockerShell()
			.Answer("docker image inspect redis:7.4", 0, Inspect)
			.Answer("docker buildx version", 0, "github.com/docker/buildx v0.17.1")
			.Answer("docker buildx imagetools inspect redis:7.4", 0, RedisImagetools);
		DockerImageUpdates updates = new(_time);

		await updates.CheckAsync(shell.Client(), ["redis:7.4"], force: false, null, Ct);
		int asked = shell.Asked.Count;
		IReadOnlyList<ImageUpdateResult> again = await updates.CheckAsync(shell.Client(), ["docker.io/library/redis:7.4"], force: false, null, Ct);
		Assert.Empty(again);
		Assert.Equal(asked, shell.Asked.Count);

		await updates.CheckAsync(shell.Client(), ["redis:7.4"], force: true, null, Ct);
		Assert.True(shell.Asked.Count > asked);
	}

	[Fact]
	public async Task AnAnswer_GoesStale_AndAPullForgetsIt()
	{
		FakeDockerShell shell = new FakeDockerShell()
			.Answer("docker image inspect redis:7.4", 0, Inspect)
			.Answer("docker buildx version", 0, "github.com/docker/buildx v0.17.1")
			.Answer("docker buildx imagetools inspect redis:7.4", 0, RedisImagetools);
		DockerImageUpdates updates = new(_time);
		await updates.CheckAsync(shell.Client(), ["redis:7.4"], force: false, null, Ct);

		_time.Advance(DockerImageUpdates.FreshFor - TimeSpan.FromSeconds(1));
		Assert.NotNull(updates.Find("redis:7.4"));
		Assert.NotNull(updates.LastChecked);
		_time.Advance(TimeSpan.FromSeconds(2));
		Assert.Null(updates.Find("redis:7.4"));

		await updates.CheckAsync(shell.Client(), ["redis:7.4"], force: false, null, Ct);
		updates.Forget(["docker.io/library/redis:7.4"]);
		Assert.Null(updates.Find("redis:7.4"));
	}

	[Fact]
	public async Task WithoutBuildx_DockerManifestAnswers()
	{
		FakeDockerShell shell = new FakeDockerShell()
			.Answer(InspectLine, 0, Inspect)
			.Answer("docker manifest inspect --verbose nginx:1.27-alpine", 0, NginxManifest)
			.Answer("docker manifest inspect --verbose redis:7.4", 0, RedisManifest);

		IReadOnlyList<ImageUpdateResult> results = await new DockerImageUpdates(_time).CheckAsync(shell.Client(), Wanted, force: false, null, Ct);

		Assert.True(results.Single(result => result.Reference == "nginx:1.27-alpine").HasUpdate);
		Assert.Equal(ImageUpdateState.UpToDate, results.Single(result => result.Reference == "redis:7.4").State);
		Assert.Single(shell.Asked, line => line == "docker buildx version");
	}

	[Fact]
	public async Task ABuildxWithoutImagetools_FallsBackToDockerManifest()
	{
		FakeDockerShell shell = new FakeDockerShell()
			.Answer("docker image inspect redis:7.4", 0, Inspect)
			.Answer("docker buildx version", 0, "github.com/docker/buildx v0.3.1")
			.Answer("docker buildx imagetools inspect redis:7.4", 1, error: "unknown command \"imagetools\" for \"docker buildx\"")
			.Answer("docker manifest inspect --verbose redis:7.4", 0, RedisManifest);

		ImageUpdateResult result = Assert.Single(await new DockerImageUpdates(_time).CheckAsync(shell.Client(), ["redis:7.4"], force: false, null, Ct));

		Assert.Equal(ImageUpdateState.UpToDate, result.State);
	}

	[Fact]
	public async Task NeitherTool_SaysSoPlainly_AndStopsAsking()
	{
		FakeDockerShell shell = new FakeDockerShell()
			.Answer(InspectLine, 0, Inspect)
			.Answer("docker image inspect nginx:1.27-alpine", 0, Inspect);
		DockerImageUpdates updates = new(_time);

		IReadOnlyList<ImageUpdateResult> results = await updates.CheckAsync(shell.Client(), Wanted, force: false, null, Ct);

		Assert.All(results.Where(result => result.Reference is "nginx:1.27-alpine" or "redis:7.4"), result =>
		{
			Assert.Equal(ImageUpdateState.Unsupported, result.State);
			Assert.Contains("docker manifest", result.Detail, StringComparison.Ordinal);
		});

		int asked = shell.Asked.Count;
		await updates.CheckAsync(shell.Client(), ["nginx:1.27-alpine"], force: true, null, Ct);
		Assert.Equal(asked + 1, shell.Asked.Count);
	}

	[Fact]
	public async Task ARegistryThatRefuses_IsOneImagesAnswer_NotTheWholeChecks()
	{
		FakeDockerShell shell = new FakeDockerShell()
			.Answer(InspectLine, 0, Inspect)
			.Answer("docker buildx version", 0, "github.com/docker/buildx v0.17.1")
			.Answer("docker buildx imagetools inspect nginx:1.27-alpine", 1, error: "ERROR: unauthorized: authentication required")
			.Answer("docker buildx imagetools inspect redis:7.4", 0, RedisImagetools);

		IReadOnlyList<ImageUpdateResult> results = await new DockerImageUpdates(_time).CheckAsync(shell.Client(), Wanted, force: false, null, Ct);

		Assert.Equal(ImageUpdateState.Refused, results.Single(result => result.Reference == "nginx:1.27-alpine").State);
		Assert.Equal(ImageUpdateState.UpToDate, results.Single(result => result.Reference == "redis:7.4").State);
	}

	[Fact]
	public async Task ADaemonThatDoesNotAnswer_FailsTheCheck()
	{
		FakeDockerShell shell = new FakeDockerShell()
			.Answer("docker image inspect redis:7.4", 1, error: "Cannot connect to the Docker daemon at unix:///var/run/docker.sock. Is the docker daemon running?");

		DockerCommandException failure = await Assert.ThrowsAsync<DockerCommandException>(() =>
			new DockerImageUpdates(_time).CheckAsync(shell.Client(), ["redis:7.4"], force: false, null, Ct));

		Assert.Equal(DockerFailure.DaemonUnreachable, failure.Failure);
	}
}
