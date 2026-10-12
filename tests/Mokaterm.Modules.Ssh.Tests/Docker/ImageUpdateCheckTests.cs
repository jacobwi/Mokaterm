using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>Reading what a registry holds for a tag, and deciding from it whether the host's image is behind.</summary>
public sealed class ImageUpdateCheckTests
{
	private const string Index = "sha256:2140dad235c130ac861018a4e13a6bc8aea3a35f3a40e20c1b060d51a7efd250";
	private const string Amd64 = "sha256:4f8b5b2b9b0e6a1f8d0f7a3f8c4e9d2b1a0c3e5f7a9b1c3d5e7f9a1b3c5d7e9f";
	private const string Arm64 = "sha256:9a8b7c6d5e4f3a2b1c0d9e8f7a6b5c4d3e2f1a0b9c8d7e6f5a4b3c2d1e0f9a8b";
	private const string Config = "sha256:c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0";
	private const string Older = "sha256:0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d0d";

	/// <summary><c>docker buildx imagetools inspect nginx:1.27-alpine</c> for a multi-platform image.</summary>
	internal const string ImagetoolsIndex = $"""
		Name:      docker.io/library/nginx:1.27-alpine
		MediaType: application/vnd.oci.image.index.v1+json
		Digest:    {Index}

		Manifests:
		  Name:        docker.io/library/nginx:1.27-alpine@{Amd64}
		  MediaType:   application/vnd.oci.image.manifest.v1+json
		  Platform:    linux/amd64

		  Name:        docker.io/library/nginx:1.27-alpine@{Arm64}
		  MediaType:   application/vnd.oci.image.manifest.v1+json
		  Platform:    linux/arm64/v8
		""";

	/// <summary><c>docker manifest inspect --verbose</c> for a single-platform image: one object whose descriptor is the tag's.</summary>
	private const string ManifestSingle = $$"""
		{
			"Ref": "docker.io/acme/tool:1.0",
			"Descriptor": { "mediaType": "application/vnd.docker.distribution.manifest.v2+json", "digest": "{{Amd64}}", "size": 528, "platform": { "architecture": "amd64", "os": "linux" } },
			"Raw": "eyJ9",
			"SchemaV2Manifest": { "schemaVersion": 2, "config": { "mediaType": "application/vnd.docker.container.image.v1+json", "size": 1472, "digest": "{{Config}}" } }
		}
		""";

	/// <summary>The same for an index: one object per platform, and the index's own digest nowhere.</summary>
	private const string ManifestList = $$"""
		[
			{ "Ref": "docker.io/library/nginx:1.27-alpine@{{Amd64}}", "Descriptor": { "digest": "{{Amd64}}", "platform": { "architecture": "amd64", "os": "linux" } }, "OCIManifest": { "config": { "digest": "{{Config}}" } } },
			{ "Ref": "docker.io/library/nginx:1.27-alpine@{{Arm64}}", "Descriptor": { "digest": "{{Arm64}}", "platform": { "architecture": "arm64", "os": "linux", "variant": "v8" } }, "OCIManifest": { "config": { "digest": "sha256:1111111111111111111111111111111111111111111111111111111111111111" } } }
		]
		""";

	private static readonly DateTimeOffset Now = new(2026, 10, 11, 7, 0, 0, TimeSpan.Zero);

	private static readonly DockerImageName Nginx = DockerImageName.Parse("nginx:1.27-alpine")!;

	[Fact]
	public void Imagetools_ReadsTheTagsDigest_AndEachPlatform()
	{
		DockerRegistryManifest manifest = DockerRegistryManifest.ParseImagetools(ImagetoolsIndex)!;

		Assert.Equal(Index, manifest.Digest);
		Assert.Equal(["linux/amd64", "linux/arm64/v8"], manifest.Platforms.Select(platform => platform.Display));
		Assert.Equal(Amd64, manifest.Platforms[0].Digest);
	}

	[Theory]
	[InlineData("")]
	[InlineData("ERROR: not found")]
	[InlineData("Digest:    sha256:XYZ")]
	public void Imagetools_WithoutADigestLine_ReadsAsNothing(string output) => Assert.Null(DockerRegistryManifest.ParseImagetools(output));

	[Fact]
	public void ManifestVerbose_OneManifest_CarriesTheTagsDigestAndConfig()
	{
		DockerRegistryManifest manifest = DockerRegistryManifest.ParseManifestVerbose(ManifestSingle)!;

		Assert.Equal(Amd64, manifest.Digest);
		Assert.Equal(Config, Assert.Single(manifest.Platforms).ConfigDigest);
	}

	[Fact]
	public void ManifestVerbose_AnIndex_HasPlatformsButNoDigestOfItsOwn()
	{
		DockerRegistryManifest manifest = DockerRegistryManifest.ParseManifestVerbose(ManifestList)!;

		Assert.Null(manifest.Digest);
		Assert.Equal(2, manifest.Platforms.Count);
		Assert.Equal("v8", manifest.Platforms[1].Variant);
	}

	[Fact]
	public void Compare_TheRegistrysDigestAmongRepoDigests_IsCurrent()
	{
		DockerLocalImage image = Local(Older, "nginx@" + Index);

		ImageUpdateResult result = ImageUpdateCheck.Compare("nginx:1.27-alpine", Nginx, image, DockerRegistryManifest.ParseImagetools(ImagetoolsIndex)!, Now);

		Assert.Equal(ImageUpdateState.UpToDate, result.State);
		Assert.False(result.HasUpdate);
		Assert.Equal(Index, result.RemoteDigest);
	}

	[Fact]
	public void Compare_ATagThatMovedOn_HasAnUpdate()
	{
		DockerLocalImage image = Local(Older, "nginx@" + Older);

		ImageUpdateResult result = ImageUpdateCheck.Compare("nginx:1.27-alpine", Nginx, image, DockerRegistryManifest.ParseImagetools(ImagetoolsIndex)!, Now);

		Assert.Equal(ImageUpdateState.UpdateAvailable, result.State);
		Assert.Contains("sha256:2140dad235c1", result.Detail, StringComparison.Ordinal);
	}

	[Fact]
	public void Compare_DigestsFromAnotherRegistry_DoNotCount()
	{
		DockerLocalImage image = Local(Older, "mirror.local/library/nginx@" + Index, "nginx@" + Older);

		Assert.True(ImageUpdateCheck.Compare("nginx:1.27-alpine", Nginx, image, DockerRegistryManifest.ParseImagetools(ImagetoolsIndex)!, Now).HasUpdate);
	}

	[Fact]
	public void Compare_AManifestList_UsesThePlatformsConfigAgainstTheImageId()
	{
		DockerRegistryManifest list = DockerRegistryManifest.ParseManifestVerbose(ManifestList)!;

		Assert.Equal(ImageUpdateState.UpToDate, ImageUpdateCheck.Compare("nginx:1.27-alpine", Nginx, Local(Config, "nginx@" + Older), list, Now).State);
		Assert.Equal(ImageUpdateState.UpdateAvailable, ImageUpdateCheck.Compare("nginx:1.27-alpine", Nginx, Local(Older, "nginx@" + Index), list, Now).State);
	}

	[Fact]
	public void Compare_AManifestList_ForAStoreThatKeepsTheIndex_SaysItCannotTell()
	{
		DockerRegistryManifest list = DockerRegistryManifest.ParseManifestVerbose(ManifestList)!;

		ImageUpdateResult result = ImageUpdateCheck.Compare("nginx:1.27-alpine", Nginx, Local(Index, "nginx@" + Index), list, Now);

		Assert.Equal(ImageUpdateState.Failed, result.State);
		Assert.Contains("buildx", result.Detail, StringComparison.Ordinal);
	}

	[Fact]
	public void Compare_NoManifestForThisPlatform_SaysTheTagLostIt()
	{
		DockerLocalImage s390 = Local(Older, "nginx@" + Older) with { Architecture = "s390x" };

		Assert.Equal(ImageUpdateState.TagGone, ImageUpdateCheck.Compare("nginx:1.27-alpine", Nginx, s390, DockerRegistryManifest.ParseManifestVerbose(ManifestList)!, Now).State);
	}

	[Fact]
	public void WithoutRegistry_LocalBuildsAndUntaggedImages_AreNeverAsked()
	{
		DockerImageName dev = DockerImageName.Parse("shop-worker:dev")!;

		Assert.Equal(ImageUpdateState.LocalOnly, ImageUpdateCheck.WithoutRegistry("shop-worker:dev", dev, Local(Older) with { RepoTags = ["shop-worker:dev"] }, Now)!.State);
		Assert.Equal(ImageUpdateState.NoTag, ImageUpdateCheck.WithoutRegistry("4ca2d821beb2", null, null, Now)!.State);
		Assert.Equal(ImageUpdateState.NoTag, ImageUpdateCheck.WithoutRegistry("nginx:gone", DockerImageName.Parse("nginx:gone"), null, Now)!.State);
		Assert.Null(ImageUpdateCheck.WithoutRegistry("nginx:1.27-alpine", Nginx, Local(Older, "nginx@" + Older), Now));
	}

	[Theory]
	[InlineData(127, "sh: 1: docker: not found", nameof(ImageUpdateState.Unsupported))]
	[InlineData(1, "docker: 'buildx' is not a docker command.\nSee 'docker --help'", nameof(ImageUpdateState.Unsupported))]
	[InlineData(1, "docker manifest inspect is only supported on a Docker cli with experimental cli features enabled", nameof(ImageUpdateState.Unsupported))]
	[InlineData(1, "ERROR: unauthorized: authentication required", nameof(ImageUpdateState.Refused))]
	[InlineData(1, "Error response from daemon: pull access denied for acme/private, repository does not exist or may require 'docker login'", nameof(ImageUpdateState.Refused))]
	[InlineData(1, "ERROR: failed to do request: Head \"https://registry.example.com/v2/x/manifests/1\": dial tcp: lookup registry.example.com: no such host", nameof(ImageUpdateState.Unreachable))]
	[InlineData(1, "ERROR: docker.io/library/nginx:9.9: not found", nameof(ImageUpdateState.TagGone))]
	[InlineData(1, "no such manifest: docker.io/library/nginx:9.9", nameof(ImageUpdateState.TagGone))]
	[InlineData(1, "something odd happened", nameof(ImageUpdateState.Failed))]
	public void Failed_SaysWhyTheRegistryWasNotAsked(int exit, string error, string state)
	{
		DockerOutput output = new() { Command = "docker buildx imagetools inspect nginx:1.27-alpine", ExitCode = exit, Error = error };

		ImageUpdateResult result = ImageUpdateCheck.Failed("nginx:1.27-alpine", Nginx, output, "build-server", Now);

		Assert.Equal(Enum.Parse<ImageUpdateState>(state), result.State);
		Assert.False(result.IsKnown);
		Assert.NotEmpty(result.Detail);
	}

	private static DockerLocalImage Local(string id, params string[] repoDigests) => new()
	{
		Id = id,
		RepoTags = ["nginx:1.27-alpine"],
		RepoDigests = repoDigests,
		Os = "linux",
		Architecture = "amd64",
	};
}
