using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>The new container form's values: what is refused and where, and the exact <c>docker run</c> they become.</summary>
public sealed class DockerRunSpecTests
{
	[Fact]
	public void TheImageAlone_RunsDetached()
	{
		DockerRunSpec spec = new() { Image = " nginx:1.27-alpine " };

		Assert.Empty(spec.Problems());
		Assert.Equal("docker run --detach nginx:1.27-alpine", spec.ToCall().Display);
		Assert.Equal(DockerCall.LongTimeout, spec.ToCall().Timeout);
	}

	[Fact]
	public void EveryField_InDockersOrder_BlankRowsLeftOut()
	{
		DockerRunSpec spec = new()
		{
			Image = "nginx:1.27-alpine",
			Name = "web",
			Ports = ["8080:80", " ", "127.0.0.1:8443:443/tcp"],
			Mounts = ["/srv/site:/usr/share/nginx/html:ro", "", "cache:/var/cache/nginx"],
			Environment = [new("TZ", "Europe/Paris"), new("", ""), new("API_TOKEN", "tok en")],
			Restart = DockerRestartPolicy.OnFailure,
			MaxRetries = 5,
			Network = "shop_default",
			Command = "nginx -g 'daemon off;'",
		};

		Assert.Equal(
			"docker run --detach --name web --restart on-failure:5 --network shop_default --publish 8080:80 --publish 127.0.0.1:8443:443/tcp "
				+ "--volume /srv/site:/usr/share/nginx/html:ro --volume cache:/var/cache/nginx --env 'TZ=••••••' --env 'API_TOKEN=••••••' "
				+ "nginx:1.27-alpine nginx -g 'daemon off;'",
			spec.ToCall().Display);
	}

	[Fact]
	public void EnvironmentValues_AreMaskedInWhatIsShown_AndRunAsTyped()
	{
		DockerCall call = new DockerRunSpec { Image = "alpine:3.20", Environment = [new("TOKEN", "s3cr3t"), new("EMPTY", "")] }.ToCall();

		Assert.Equal("docker run --detach --env 'TOKEN=••••••' --env EMPTY= alpine:3.20", call.Display);
		Assert.Equal("docker run --detach --env TOKEN=s3cr3t --env EMPTY= alpine:3.20", DockerCommandLine.Build(call));
		Assert.DoesNotContain("s3cr3t", call.ToString(), StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(nameof(DockerRestartPolicy.No), "")]
	[InlineData(nameof(DockerRestartPolicy.Always), "--restart always ")]
	[InlineData(nameof(DockerRestartPolicy.UnlessStopped), "--restart unless-stopped ")]
	[InlineData(nameof(DockerRestartPolicy.OnFailure), "--restart on-failure ")]
	public void RestartPolicies(string policy, string words) =>
		Assert.Equal($"docker run --detach {words}redis:7.4", new DockerRunSpec { Image = "redis:7.4", Restart = Enum.Parse<DockerRestartPolicy>(policy) }.ToCall().Display);

	[Fact]
	public void Problems_AreKeptAgainstTheirFields()
	{
		DockerRunSpec spec = new()
		{
			Image = "Nginx",
			Name = "web",
			Ports = ["8080:80", "80"],
			Mounts = ["relative:path"],
			Environment = [new("TZ", "UTC"), new("2BAD", "x"), new("TZ", "Europe/Paris")],
			Restart = DockerRestartPolicy.OnFailure,
			MaxRetries = -1,
			Network = "-n",
			Command = "echo 'unclosed",
		};

		IReadOnlyDictionary<string, string> problems = spec.Problems(takenNames: ["web"]);

		Assert.Equal(
			["command", "env:1", "env:2", "image", "mount:0", "name", "network", "port:1", "restart"],
			problems.Keys.Order(StringComparer.Ordinal));
		Assert.Contains("exists already", problems[DockerRunSpec.NameField], StringComparison.Ordinal);
		Assert.Contains("set twice", problems[DockerRunSpec.VariableField(2)], StringComparison.Ordinal);
		Assert.Throws<ArgumentException>(() => spec.ToCall());
	}

	[Theory]
	[InlineData("host")]
	[InlineData("none")]
	public void PortsOnANetworkThatPublishesNothing_AreRefused(string network)
	{
		DockerRunSpec spec = new() { Image = "nginx", Network = network, Ports = ["8080:80"] };

		Assert.Contains("nothing is published", spec.Problems()[DockerRunSpec.NetworkField], StringComparison.Ordinal);
		Assert.Empty((spec with { Ports = [] }).Problems());
	}

	[Fact]
	public void TheSummary_SaysWhatHappensAndThatValuesAreHidden()
	{
		Assert.StartsWith("Creates and starts web from nginx", new DockerRunSpec { Image = "nginx", Name = "web" }.Summary(), StringComparison.Ordinal);
		Assert.Contains("hidden", new DockerRunSpec { Image = "nginx", Environment = [new("A", "1")] }.Summary(), StringComparison.Ordinal);
		Assert.Contains("docker names itself", new DockerRunSpec { Image = "nginx" }.Summary(), StringComparison.Ordinal);
	}

	[Fact]
	public void NeitherTheCommandNorAValue_IsPrinted()
	{
		string printed = new DockerRunSpec { Image = "nginx", Command = "curl -H 'token: abc'", Environment = [new("TOKEN", "abc")] }.ToString();

		Assert.DoesNotContain("abc", printed, StringComparison.Ordinal);
		Assert.DoesNotContain("abc", new DockerRunVariable("TOKEN", "abc").ToString(), StringComparison.Ordinal);
	}
}
