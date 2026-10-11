using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Core.Tests.Abstractions;

/// <summary>Docker's own naming rules, which every name passes before it reaches a docker command line.</summary>
public sealed class DockerNamesTests
{
	[Theory]
	[InlineData("web")]
	[InlineData("shop-web-1")]
	[InlineData("a.b_c-d")]
	[InlineData("f27c9f265ce1")]
	[InlineData("0a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f9")]
	public void Container_DockersRule_Passes(string name) => Assert.Null(DockerNames.ContainerProblem(name));

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("x")]
	[InlineData("-rf")]
	[InlineData("_web")]
	[InlineData("my web")]
	[InlineData(" web")]
	[InlineData("web\n")]
	[InlineData("web/1")]
	[InlineData("web;id")]
	public void Container_AnythingElse_IsRefused(string? name) => Assert.NotNull(DockerNames.ContainerProblem(name));

	[Fact]
	public void Container_TooLong_IsRefused()
	{
		Assert.Null(DockerNames.ContainerProblem(new string('a', DockerNames.MaxNameLength)));
		Assert.NotNull(DockerNames.ContainerProblem(new string('a', DockerNames.MaxNameLength + 1)));
	}

	[Theory]
	[InlineData("nginx")]
	[InlineData("nginx:1.27-alpine")]
	[InlineData("library/nginx:latest")]
	[InlineData("ghcr.io/acme/shop-api:2.4.1")]
	[InlineData("localhost:5000/app")]
	[InlineData("registry.local:5000/team/app@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
	[InlineData("sha256:4ca2d821beb2aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
	[InlineData("4ca2d821beb2")]
	public void Image_EveryReferenceForm_Passes(string reference) => Assert.Null(DockerNames.ImageProblem(reference));

	[Theory]
	[InlineData("Nginx")]
	[InlineData("-v")]
	[InlineData("nginx:")]
	[InlineData("nginx latest")]
	[InlineData("nginx@sha256:short")]
	[InlineData("nginx:-tag")]
	public void Image_AnythingElse_IsRefused(string reference) => Assert.NotNull(DockerNames.ImageProblem(reference));

	[Theory]
	[InlineData("shop", true)]
	[InlineData("my_project-2", true)]
	[InlineData("Shop", false)]
	[InlineData("-shop", false)]
	[InlineData("shop.v2", false)]
	public void Project_ComposesRule(string name, bool passes) => Assert.Equal(passes, DockerNames.ProjectProblem(name) is null);

	[Theory]
	[InlineData("web", true)]
	[InlineData("Api.v2_worker-1", true)]
	[InlineData("-d", false)]
	[InlineData("api;rm", false)]
	public void Service_ComposesRule(string name, bool passes) => Assert.Equal(passes, DockerNames.ServiceProblem(name) is null);

	[Theory]
	[InlineData("shop_pgdata", true)]
	[InlineData("bridge", true)]
	[InlineData("host", true)]
	[InlineData("plugin:vol@1+x", true)]
	[InlineData("-v", false)]
	[InlineData("a b", false)]
	public void ExistingName_RefusesOnlyWhatACommandLineShouldNotCarry(string name, bool passes) =>
		Assert.Equal(passes, DockerNames.ExistingNameProblem(name, "volume") is null);

	[Theory]
	[InlineData("/srv/shop", true)]
	[InlineData("/srv/my shop/compose.yaml", true)]
	[InlineData("srv/shop", false)]
	[InlineData("/srv/a\nb", false)]
	[InlineData("", false)]
	public void Path_AbsoluteAndClean(string path, bool passes) => Assert.Equal(passes, DockerNames.PathProblem(path, "folder") is null);

	[Theory]
	[InlineData("f27c9f265ce1", true)]
	[InlineData("sha256:f27c9f265ce1aa", true)]
	[InlineData("f27c9f26", false)]
	[InlineData("web", false)]
	public void IsId(string value, bool expected) => Assert.Equal(expected, DockerNames.IsId(value));

	[Fact]
	public void ContainerRow_ReadsAPsLine()
	{
		IReadOnlyList<DockerContainerRow> rows = DockerContainerRow.Parse("""
			{"ID":"f27c9f265ce1","Names":"web,old/alias","Image":"nginx","Command":"\"nginx -g 'daemon off;'\"","Ports":"80/tcp","Status":"Up 2 hours (Paused)","Labels":"a=b"}
			not json
			{"Names":"no-id"}
			""");

		DockerContainerRow row = Assert.Single(rows);
		Assert.Equal("web", row.Name);
		Assert.Equal("paused", row.State);
		Assert.False(row.IsRunning);
		Assert.DoesNotContain("daemon off", row.ToString(), StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("{\"a\":1}\n{\"a\":2}\n", 2)]
	[InlineData("[{\"a\":1},{\"a\":2},3]", 2)]
	[InlineData("{\n  \"a\": {\n    \"b\": 1\n  }\n}\n", 1)]
	[InlineData("[\n  {\n    \"a\": 1\n  }\n]", 1)]
	[InlineData("warning: something\n{\"a\":1}\n", 1)]
	[InlineData("[]", 0)]
	[InlineData("   ", 0)]
	public void JsonOutput_ReadsLinesArraysAndIndentedValues(string output, int count) =>
		Assert.Equal(count, DockerJsonOutput.ReadObjects(output).Count);

	[Theory]
	[InlineData("Up 2 hours", "running")]
	[InlineData("Up 2 hours (Paused)", "paused")]
	[InlineData("Exited (0) 2 days ago", "exited")]
	[InlineData("Created", "created")]
	[InlineData("Restarting (1) 3 seconds ago", "restarting")]
	[InlineData("Removal In Progress", "removing")]
	[InlineData("Dead", "dead")]
	[InlineData("", "")]
	public void StateFromStatus_ForAnOlderDocker(string status, string state) => Assert.Equal(state, DockerContainerRow.StateFromStatus(status));
}
