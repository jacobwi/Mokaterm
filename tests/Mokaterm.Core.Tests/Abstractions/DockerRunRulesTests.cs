using Mokaterm.Abstractions.Containers;

namespace Mokaterm.Core.Tests.Abstractions;

/// <summary>What a new container is given besides names: published ports, mounts, variable names and retries.</summary>
public sealed class DockerRunRulesTests
{
	[Theory]
	[InlineData("8080:80")]
	[InlineData("8080:80/tcp")]
	[InlineData("5353:53/udp")]
	[InlineData("127.0.0.1:8443:443")]
	[InlineData("0.0.0.0:80:80/sctp")]
	[InlineData("[::1]:8080:80")]
	[InlineData("8000-8009:8000-8009")]
	[InlineData("8000-8009:80")]
	[InlineData(" 8080:80 ")]
	public void PortMapping_DockersForms_Pass(string mapping) => Assert.Null(DockerNames.PortMappingProblem(mapping));

	[Theory]
	[InlineData("", "Name a port")]
	[InlineData("80", "[ip:]host:container")]
	[InlineData("8080:80/http", "[ip:]host:container")]
	[InlineData("-p 8080:80", "[ip:]host:container")]
	[InlineData("8080:80;id", "[ip:]host:container")]
	[InlineData("0:80", "host port 0")]
	[InlineData("70000:80", "host port 70000")]
	[InlineData("8080:0", "container port 0")]
	[InlineData("9000-8000:80", "host port 9000-8000")]
	[InlineData("8080:80-81", "same length")]
	[InlineData("8000-8001:80-82", "same length")]
	[InlineData("999.1.1.1:80:80", "not an IP address")]
	[InlineData("1:80:80", "not an IP address")]
	[InlineData("[::1:80:80", "[ip:]host:container")]
	public void PortMapping_AnythingElse_SaysWhy(string mapping, string expected) =>
		Assert.Contains(expected, DockerNames.PortMappingProblem(mapping), StringComparison.Ordinal);

	[Theory]
	[InlineData("/data")]
	[InlineData("data:/var/lib/data")]
	[InlineData("shop_pgdata:/var/lib/postgresql/data:rw")]
	[InlineData("/srv/site:/usr/share/nginx/html:ro")]
	[InlineData("/srv/site:/usr/share/nginx/html:ro,z")]
	[InlineData("/srv/my site:/site")]
	public void Mount_DockersForms_Pass(string mount) => Assert.Null(DockerNames.MountProblem(mount));

	[Theory]
	[InlineData("", "Name a volume")]
	[InlineData("data", "not an absolute path")]
	[InlineData("data:relative", "not an absolute path")]
	[InlineData("-v:/data", "cannot start with '-'")]
	[InlineData("x:/data", "at least two characters")]
	[InlineData("/a:/b:/c:/d", "source:target")]
	[InlineData("/a:/b:rx", "rx is not a mount option")]
	[InlineData("/a:/b:", "An empty option")]
	[InlineData("/a:/b:ro,rw", "either ro or rw")]
	[InlineData("/a\nb:/b", "control character")]
	public void Mount_AnythingElse_SaysWhy(string mount, string expected) =>
		Assert.Contains(expected, DockerNames.MountProblem(mount), StringComparison.Ordinal);

	[Theory]
	[InlineData("A", true)]
	[InlineData("_private", true)]
	[InlineData("DB_PASSWORD2", true)]
	[InlineData("", false)]
	[InlineData("2FAST", false)]
	[InlineData("WITH-DASH", false)]
	[InlineData("A B", false)]
	[InlineData("A=B", false)]
	public void Variable_LettersDigitsAndUnderscore(string name, bool valid) =>
		Assert.Equal(valid, DockerNames.VariableProblem(name) is null);

	[Theory]
	[InlineData(0, true)]
	[InlineData(5, true)]
	[InlineData(DockerNames.MaxRestartRetries, true)]
	[InlineData(-1, false)]
	[InlineData(DockerNames.MaxRestartRetries + 1, false)]
	public void RestartRetries_FromNoLimitToTheCap(int retries, bool valid) =>
		Assert.Equal(valid, DockerNames.RestartRetriesProblem(retries) is null);
}
