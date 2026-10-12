using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>References read the way docker reads them, so two spellings of one image meet.</summary>
public sealed class DockerImageNameTests
{
	[Theory]
	[InlineData("nginx", "docker.io/library/nginx:latest")]
	[InlineData("nginx:1.27-alpine", "docker.io/library/nginx:1.27-alpine")]
	[InlineData("library/nginx:1.27", "docker.io/library/nginx:1.27")]
	[InlineData("docker.io/library/nginx:1.27", "docker.io/library/nginx:1.27")]
	[InlineData("index.docker.io/library/nginx", "docker.io/library/nginx:latest")]
	[InlineData("grafana/grafana:11.2.0", "docker.io/grafana/grafana:11.2.0")]
	[InlineData("ghcr.io/acme/shop-api:2.4.1", "ghcr.io/acme/shop-api:2.4.1")]
	[InlineData("localhost:5000/app", "localhost:5000/app:latest")]
	[InlineData("localhost/app:dev", "localhost/app:dev")]
	[InlineData("registry.local:5000/team/app:1", "registry.local:5000/team/app:1")]
	public void Canonical_IsWhatDockerMeans(string reference, string canonical) =>
		Assert.Equal(canonical, DockerImageName.Parse(reference)!.Canonical);

	[Fact]
	public void ADigest_PinsWithoutATag()
	{
		const string Digest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
		DockerImageName name = DockerImageName.Parse("nginx@" + Digest)!;

		Assert.Null(name.Tag);
		Assert.Equal(Digest, name.Digest);
		Assert.Equal("docker.io/library/nginx@" + Digest, name.Canonical);
	}

	[Theory]
	[InlineData("")]
	[InlineData("4ca2d821beb2")]
	[InlineData("sha256:4ca2d821beb2aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
	[InlineData("Nginx")]
	[InlineData("-v")]
	public void IdsAndNonsense_AreNoName(string reference) => Assert.Null(DockerImageName.Parse(reference));

	[Fact]
	public void Key_JoinsSpellings_AndLeavesAnIdAsItIs()
	{
		Assert.Equal(DockerImageName.Key("nginx"), DockerImageName.Key("docker.io/library/nginx:latest"));
		Assert.Equal("4ca2d821beb2", DockerImageName.Key(" 4ca2d821beb2 "));
	}

	[Fact]
	public void DigestIn_ReadsOnlyThisRepositorysEntries()
	{
		DockerImageName name = DockerImageName.Parse("nginx:1.27-alpine")!;
		const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

		Assert.Equal(Digest, name.DigestIn("nginx@" + Digest));
		Assert.Equal(Digest, name.DigestIn("docker.io/library/nginx@" + Digest));
		Assert.Null(name.DigestIn("mirror.local/library/nginx@" + Digest));
		Assert.Null(name.DigestIn("nginx:1.27-alpine"));
	}
}
