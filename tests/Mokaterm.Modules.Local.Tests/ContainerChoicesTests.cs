using Mokaterm.Modules.Local.Containers;

namespace Mokaterm.Modules.Local.Tests;

/// <summary>What docker ps and kubectl get pods print, read into the choices the editor offers.</summary>
public sealed class ContainerChoicesTests
{
	private const string DockerPs = """
		{"Command":"\"docker-entrypoint.s…\"","CreatedAt":"2026-10-09 07:00:00 +0000 UTC","ID":"f27c9f265ce1","Image":"postgres:16","Labels":"","Names":"shop-db-1","Ports":"5432/tcp","RunningFor":"2 days ago","State":"running","Status":"Up 2 days (healthy)"}
		WARNING: the "remote" context's certificate expires in 3 days
		{"Command":"\"/run.sh\"","CreatedAt":"2026-10-09 07:00:00 +0000 UTC","ID":"0a1b2c3d4e5f","Image":"grafana/grafana:11.2.0","Names":"monitoring-grafana-1","State":"paused","Status":"Up 2 days (Paused)"}
		{"Command":"\"nginx\"","ID":"9f8e7d6c5b4a","Image":"nginx:1.27-alpine","Names":"a.b_c-d","Status":"Up 3 minutes"}
		""";

	private const string Pods = """
		{
		  "apiVersion": "v1",
		  "kind": "List",
		  "items": [
		    {
		      "metadata": { "name": "web-7d9f8b6c5-x2k4q", "namespace": "shop" },
		      "spec": { "containers": [ { "name": "app" }, { "name": "envoy" } ] },
		      "status": { "phase": "Running" }
		    },
		    {
		      "metadata": { "name": "migrate-q8z7w", "namespace": "shop" },
		      "spec": { "containers": [ { "name": "migrate" } ] },
		      "status": { "phase": "Succeeded" }
		    },
		    {
		      "metadata": { "name": "api-0", "namespace": "backend" },
		      "spec": { "containers": [ { "name": "api" } ] },
		      "status": { "phase": "Pending" }
		    },
		    { "metadata": { "namespace": "shop" } },
		    "not an object"
		  ]
		}
		""";

	[Fact]
	public void FromDockerPs_ReadsEveryObjectLine_AndSkipsTheRest()
	{
		IReadOnlyList<ContainerChoice> choices = ContainerChoices.FromDockerPs(DockerPs);

		Assert.Equal(["a.b_c-d", "monitoring-grafana-1", "shop-db-1"], choices.Select(choice => choice.Name));
		ContainerChoice db = choices[2];
		Assert.Equal("postgres:16 · Up 2 days (healthy)", db.Detail);
		Assert.True(db.IsRunning);
		Assert.Null(db.Namespace);
		Assert.Empty(db.Containers);
	}

	[Fact]
	public void FromDockerPs_APausedContainer_IsNotRunning_AndAMissingStateComesFromTheStatus()
	{
		IReadOnlyList<ContainerChoice> choices = ContainerChoices.FromDockerPs(DockerPs);

		Assert.False(choices.Single(choice => choice.Name == "monitoring-grafana-1").IsRunning);
		Assert.True(choices.Single(choice => choice.Name == "a.b_c-d").IsRunning);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("\n\n")]
	[InlineData("Cannot connect to the Docker daemon")]
	public void FromDockerPs_NothingListed_IsEmpty(string? output) => Assert.Empty(ContainerChoices.FromDockerPs(output));

	[Fact]
	public void FromKubectlPods_ReadsNameNamespacePhaseAndContainers()
	{
		IReadOnlyList<ContainerChoice> choices = ContainerChoices.FromKubectlPods(Pods);

		Assert.Equal(["api-0", "migrate-q8z7w", "web-7d9f8b6c5-x2k4q"], choices.Select(choice => choice.Name));
		ContainerChoice web = choices[2];
		Assert.Equal("shop", web.Namespace);
		Assert.True(web.IsRunning);
		Assert.Equal(["app", "envoy"], web.Containers);
		Assert.Equal("Running · 2 containers", web.Detail);
		Assert.False(choices[0].IsRunning);
		Assert.Equal("Pending", choices[0].Detail);
		Assert.Equal("backend", choices[0].Namespace);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("{\"kind\":\"List\",\"items\":[]}")]
	[InlineData("{\"kind\":\"List\",\"items\":")]
	[InlineData("[]")]
	[InlineData("error: You must be logged in to the server (Unauthorized)")]
	public void FromKubectlPods_NothingUsable_IsEmpty(string? json) => Assert.Empty(ContainerChoices.FromKubectlPods(json));
}
