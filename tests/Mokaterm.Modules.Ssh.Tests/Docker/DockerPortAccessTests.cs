using Mokaterm.Modules.Ssh.Docker;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>
/// Which published ports the Open button offers and how each one is reached: a tunnel on the desktop, a direct link on
/// the web host for a port published everywhere, and a sentence for the rest.
/// </summary>
public sealed class DockerPortAccessTests
{
	[Fact]
	public void Openable_KeepsPublishedTcpPorts_EachMappingOnce()
	{
		IReadOnlyList<DockerPort> ports = DockerPorts.Parse(
			"0.0.0.0:8080->80/tcp, :::8080->80/tcp, 127.0.0.1:5000->5000/tcp, 0.0.0.0:53->53/udp, 0.0.0.0:7000-7002->7000-7002/tcp, 5432/tcp");

		IReadOnlyList<DockerPort> openable = DockerPortAccess.Openable(ports);

		Assert.Equal(["8080→80/tcp", "127.0.0.1:5000→5000/tcp"], openable.Select(port => port.Display));
	}

	[Fact]
	public void Openable_TreatsTheTwoEverywhereAddressesAsOne()
	{
		DockerPort[] ports = [new("::", "8080", "80", "tcp"), new("0.0.0.0", "8080", "80", "tcp"), new("", "8080", "80", "tcp")];

		Assert.Single(DockerPortAccess.Openable(ports));
	}

	[Theory]
	[InlineData("0.0.0.0", "127.0.0.1")]
	[InlineData("", "127.0.0.1")]
	[InlineData("::", "127.0.0.1")]
	[InlineData("127.0.0.1", "127.0.0.1")]
	[InlineData("[::1]", "::1")]
	[InlineData("10.0.0.5", "10.0.0.5")]
	public void OnTheDesktop_EveryPublishedPort_GoesThroughATunnelToWhereItListens(string address, string target)
	{
		DockerPortRoute route = DockerPortAccess.Route(new DockerPort(address, "8080", "80", "tcp"), desktop: true, "docker.example", throughJumpHosts: true);

		Assert.Equal(DockerPortRouteKind.Tunnel, route.Kind);
		Assert.Equal(target, route.Host);
		Assert.Equal(8080, route.HostPort);
		Assert.Null(route.Reason);
	}

	[Fact]
	public void OnTheDesktop_TheLinkIsThisComputersEndOfTheTunnel()
	{
		DockerPortRoute route = DockerPortAccess.Route(new DockerPort("0.0.0.0", "8080", "80", "tcp"), desktop: true, "docker.example", throughJumpHosts: false);

		Assert.Equal("http://127.0.0.1:51234/", route.Link("http", 51234)?.AbsoluteUri);
		Assert.Null(route.Link("http"));
	}

	[Fact]
	public void OnTheWeb_APortPublishedEverywhere_OpensAtTheSessionsHost()
	{
		DockerPortRoute route = DockerPortAccess.Route(new DockerPort("0.0.0.0", "8080", "80", "tcp"), desktop: false, "docker.example", throughJumpHosts: false);

		Assert.Equal(DockerPortRouteKind.Direct, route.Kind);
		Assert.Equal("http://docker.example:8080/", route.Link("http")?.AbsoluteUri);
	}

	[Fact]
	public void OnTheWeb_AnIpv6Host_IsBracketedInTheLink()
	{
		DockerPortRoute route = DockerPortAccess.Route(new DockerPort("", "8443", "8443", "tcp"), desktop: false, "2001:db8::7", throughJumpHosts: false);

		Assert.Equal("https://[2001:db8::7]:8443/", route.Link("https")?.AbsoluteUri);
	}

	[Theory]
	[InlineData("127.0.0.1", "only the server itself")]
	[InlineData("[::1]", "only the server itself")]
	[InlineData("10.0.0.5", "not on every interface")]
	public void OnTheWeb_APortBoundToOneAddress_SaysWhyItCannotOpen(string address, string reason)
	{
		DockerPortRoute route = DockerPortAccess.Route(new DockerPort(address, "5000", "5000", "tcp"), desktop: false, "docker.example", throughJumpHosts: false);

		Assert.Equal(DockerPortRouteKind.Refused, route.Kind);
		Assert.False(route.CanOpen);
		Assert.Contains(reason, route.Reason, StringComparison.Ordinal);
		Assert.Empty(route.Schemes);
		Assert.Null(route.Link("http"));
	}

	[Fact]
	public void OnTheWeb_ALoginThroughJumpHosts_CannotOpenTheServersAddress()
	{
		DockerPortRoute route = DockerPortAccess.Route(new DockerPort("0.0.0.0", "8080", "80", "tcp"), desktop: false, "10.1.2.3", throughJumpHosts: true);

		Assert.Equal(DockerPortRouteKind.Refused, route.Kind);
		Assert.Contains("jump hosts", route.Reason, StringComparison.Ordinal);
	}

	// The login runs on the web server, so its loopback is the server's while the browser's is the user's own computer.
	[Theory]
	[InlineData("127.0.0.1")]
	[InlineData("localhost")]
	[InlineData("::1")]
	public void OnTheWeb_ALoginToTheWebServerItself_CannotOpenItsLoopback(string sessionHost)
	{
		DockerPortRoute route = DockerPortAccess.Route(new DockerPort("0.0.0.0", "8080", "80", "tcp"), desktop: false, sessionHost, throughJumpHosts: false);

		Assert.Equal(DockerPortRouteKind.Refused, route.Kind);
		Assert.Contains("your own computer", route.Reason, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("53", "udp")]
	[InlineData("7000-7002", "tcp")]
	public void APortWithNoOnePageToOpen_IsRefused(string hostPort, string protocol)
	{
		DockerPortRoute route = DockerPortAccess.Route(new DockerPort("0.0.0.0", hostPort, hostPort, protocol), desktop: true, "docker.example", throughJumpHosts: false);

		Assert.Equal(DockerPortRouteKind.Refused, route.Kind);
	}

	[Theory]
	[InlineData("80", "32768", "http")]
	[InlineData("443", "8080", "https")]
	[InlineData("1234", "8443", "https")]
	[InlineData("1234", "8080", "http")]
	[InlineData("8443", "443", "https")]
	public void TheScheme_ComesFromTheContainersPortFirst(string inside, string published, string scheme)
	{
		DockerPort port = new("0.0.0.0", published, inside, "tcp");

		Assert.Equal([scheme], DockerPortAccess.SchemesFor(port, int.Parse(published, System.Globalization.CultureInfo.InvariantCulture)));
	}

	[Fact]
	public void APortThatSaysNothing_LeavesTheSchemeToTheUser()
	{
		DockerPort port = new("0.0.0.0", "15432", "5432", "tcp");

		Assert.Equal(["http", "https"], DockerPortAccess.SchemesFor(port, 15432));
	}

	[Theory]
	[InlineData("8080", 8080)]
	[InlineData("65535", 65535)]
	[InlineData("0", null)]
	[InlineData("70000", null)]
	[InlineData("8000-8010", null)]
	[InlineData("+80", null)]
	[InlineData("", null)]
	public void SinglePort_ReadsOnePortOnly(string text, int? port) => Assert.Equal(port, DockerPortAccess.SinglePort(text));
}
