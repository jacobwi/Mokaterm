using Mokaterm.Modules.Ssh.Components.Docker;
using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>What <c>system df --verbose</c> says is on disk, and exactly what each cleanup removes.</summary>
public sealed class DockerCleanupTests
{
	/// <summary>Docker 27's <c>system df --verbose --format '{{json .}}'</c>, trimmed to a few of each.</summary>
	private const string Usage = """
		{"BuildCache":[{"CacheType":"regular","CreatedSince":"6 days ago","Description":"[build 3/6] RUN apt-get update","ID":"q2cm6v3xjqg5wzwn3bwt8hduq","InUse":"false","LastUsedSince":"6 days ago","Shared":"false","Size":"182MB","UsageCount":"2"},{"CacheType":"regular","Description":"[final 2/2] COPY --from=build /app /app","ID":"k1d9lzv4m7w2","InUse":"true","Size":"9.4MB"}],
		"Containers":[{"Command":"\"nginx\"","ID":"0a1b2c3d4e5f60718293a4b5c6d7e8f9","Image":"nginx:1.27-alpine","Labels":"","Names":"web","Size":"1.09kB","State":"running","Status":"Up 2 hours"},{"ID":"9f8e7d6c5b4a39281706f5e4d3c2b1a0","Image":"redis:7.4","Names":"redis","Size":"18.4kB","State":"exited","Status":"Exited (0) 3 days ago"},{"ID":"aabbccddeeff0011","Image":"alpine:3.20","Names":"seed","Size":"0B","State":"created","Status":"Created"}],
		"Images":[{"Containers":"1","CreatedSince":"20 days ago","Digest":"<none>","ID":"sha256:aaaa1111aaaa1111aaaa1111aaaa1111aaaa1111aaaa1111aaaa1111aaaa1111","Repository":"nginx","SharedSize":"7.8MB","Size":"48.3MB","Tag":"1.27-alpine","UniqueSize":"40.5MB"},{"Containers":"0","CreatedSince":"9 days ago","ID":"sha256:dddd4444dddd4444dddd4444dddd4444dddd4444dddd4444dddd4444dddd4444","Repository":"<none>","Size":"198MB","Tag":"<none>","UniqueSize":"198MB"},{"Containers":"0","CreatedSince":"90 days ago","ID":"sha256:eeee5555eeee5555eeee5555eeee5555eeee5555eeee5555eeee5555eeee5555","Repository":"busybox","Size":"4.26MB","Tag":"latest","UniqueSize":"4.26MB"},{"Containers":"N/A","ID":"sha256:ffff6666ffff6666ffff6666ffff6666ffff6666ffff6666ffff6666ffff6666","Repository":"mystery","Size":"1MB","Tag":"1"}],
		"Volumes":[{"Driver":"local","Labels":"com.docker.compose.project=shop","Links":"1","Mountpoint":"/var/lib/docker/volumes/shop_pgdata/_data","Name":"shop_pgdata","Size":"412.6MB"},{"Driver":"local","Labels":"","Links":"0","Name":"old-cache","Size":"212MB"},{"Driver":"local","Labels":"com.docker.volume.anonymous=","Links":"0","Name":"2e96105c29d2","Size":"1.2MB"},{"Driver":"local","Labels":"","Links":"0","Name":"73d68809233d210fefa83481fe8ebfe627494328a9486933cad1b3c4b8821ab4","Size":"0B"},{"Driver":"local","Labels":"","Links":"N/A","Name":"unknown-links","Size":"1MB"}]}
		""";

	private static DockerSystemUsage Read() => DockerSystemUsage.Parse(Usage.ReplaceLineEndings(""))!;

	[Fact]
	public void Parse_ReadsAllFourKinds_WithTheListingsOwnParsers()
	{
		DockerSystemUsage usage = Read();

		Assert.Equal(4, usage.Images.Count);
		Assert.Equal(1, usage.Images[0].ContainerCount);
		Assert.Equal(40_500_000, usage.Images[0].FreedBytes);
		Assert.Null(usage.Images[3].ContainerCount);
		Assert.Equal(["web", "redis", "seed"], usage.Containers.Select(container => container.Name));
		Assert.True(usage.Volumes[0].InUse);
		Assert.False(usage.Volumes[1].InUse);
		Assert.True(usage.Volumes[2].IsAnonymous);
		Assert.True(usage.Volumes[3].IsAnonymous);
		Assert.True(usage.Volumes[4].InUse);
		Assert.Equal(2, usage.BuildCache.Count);
		Assert.True(usage.BuildCache[1].InUse);
	}

	[Theory]
	[InlineData("")]
	[InlineData("TYPE      TOTAL     ACTIVE    SIZE      RECLAIMABLE")]
	[InlineData("{\"Type\":\"Images\",\"TotalCount\":\"8\"}")]
	public void Parse_AnythingButTheVerboseObject_IsNothing(string output) => Assert.Null(DockerSystemUsage.Parse(output));

	[Fact]
	public void Containers_OnlyStoppedOnesGo_ByName()
	{
		IReadOnlyList<DockerContainer> stopped = DockerCleanupPlans.StoppedContainers(Read());
		DockerChangePlan plan = DockerCleanupPlans.RemoveContainers(stopped)!;

		Assert.Equal(["redis", "seed"], stopped.Select(container => container.Name));
		Assert.Equal("docker rm redis seed", plan.Call.Display);
		Assert.True(plan.Destructive);
		Assert.Contains("redis, seed", plan.Confirm, StringComparison.Ordinal);
	}

	[Fact]
	public void Images_DanglingOrUnused_NeverOneWhoseContainersAreUncounted()
	{
		DockerSystemUsage usage = Read();

		DockerImage dangling = Assert.Single(DockerCleanupPlans.Images(usage, tagged: false));
		Assert.Equal("sha256:dddd4444dddd4444dddd4444dddd4444dddd4444dddd4444dddd4444dddd4444", dangling.Id);

		IReadOnlyList<DockerImage> unused = DockerCleanupPlans.Images(usage, tagged: true);
		Assert.Equal(["dddd4444dddd", "eeee5555eeee"], unused.Select(image => image.ShortId));

		DockerChangePlan plan = DockerCleanupPlans.RemoveImages(unused, tagged: true)!;
		Assert.Equal("docker image rm sha256:dddd4444dddd4444dddd4444dddd4444dddd4444dddd4444dddd4444dddd4444 busybox:latest", plan.Call.Display);
		Assert.Equal("Remove unused images", plan.Title);
		Assert.Contains("tagged ones included", plan.Confirm, StringComparison.Ordinal);
	}

	[Fact]
	public void Volumes_AnonymousFirst_NamedOnlyWhenAsked_AndTheDataLossIsSaid()
	{
		DockerSystemUsage usage = Read();

		Assert.Equal(2, DockerCleanupPlans.Volumes(usage, named: false).Count);
		IReadOnlyList<DockerVolume> all = DockerCleanupPlans.Volumes(usage, named: true);
		Assert.Equal(["old-cache", "2e96105c29d2", "73d68809233d210fefa83481fe8ebfe627494328a9486933cad1b3c4b8821ab4"], all.Select(volume => volume.Name));

		DockerChangePlan plan = DockerCleanupPlans.RemoveVolumes(all)!;
		Assert.StartsWith("docker volume rm old-cache 2e96105c29d2 ", plan.Call.Display, StringComparison.Ordinal);
		Assert.True(plan.Destructive);
		Assert.Contains("lost for good", plan.Confirm, StringComparison.Ordinal);
	}

	[Fact]
	public void BuildCache_IsTheOnePrune_AndListsWhatNothingUses()
	{
		IReadOnlyList<DockerBuildCacheEntry> unused = DockerCleanupPlans.BuildCache(Read());
		DockerChangePlan plan = DockerCleanupPlans.ClearBuildCache(unused)!;

		Assert.Equal("q2cm6v3xjqg5", Assert.Single(unused).ShortId);
		Assert.Equal("docker builder prune --all --force", plan.Call.Display);
		Assert.Contains("1 entry", plan.Confirm, StringComparison.Ordinal);
	}

	[Fact]
	public void NothingToRemove_IsNoPlan()
	{
		Assert.Null(DockerCleanupPlans.RemoveContainers([]));
		Assert.Null(DockerCleanupPlans.RemoveImages([], tagged: false));
		Assert.Null(DockerCleanupPlans.RemoveVolumes([]));
		Assert.Null(DockerCleanupPlans.ClearBuildCache([]));
	}
}
