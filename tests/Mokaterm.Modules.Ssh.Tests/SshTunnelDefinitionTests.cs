using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ssh.Tunnels;

namespace Mokaterm.Modules.Ssh.Tests;

public sealed class SshTunnelDefinitionTests
{
	[Theory]
	[InlineData(SshTunnelKind.Local)]
	[InlineData(SshTunnelKind.Remote)]
	[InlineData(SshTunnelKind.Dynamic)]
	public void Create_StartsFromDefaultsThatCanBeSavedStraightAway(SshTunnelKind kind)
	{
		SshTunnelDefinition tunnel = SshTunnelDefinition.Create(kind);

		Assert.NotEqual(Guid.Empty, tunnel.Id);
		Assert.Equal("127.0.0.1", tunnel.ListenHost);
		Assert.True(tunnel.OpenWithSession);
		Assert.Null(tunnel.Validate());

		// A new row has to survive the write back to the connection, which drops anything that would not open.
		Assert.Equal(tunnel, Assert.Single(SshConnectionOptions.FromOptions(new SshConnectionOptions { Tunnels = [tunnel] }.ApplyTo(ProtocolOptions.Empty)).Tunnels));
	}

	[Fact]
	public void Create_GivesOnlyForwardingTunnelsADestination()
	{
		Assert.True(SshTunnelDefinition.Create(SshTunnelKind.Local).HasDestination);
		Assert.Equal("localhost", SshTunnelDefinition.Create(SshTunnelKind.Local).DestinationHost);
		Assert.False(SshTunnelDefinition.Create(SshTunnelKind.Dynamic).HasDestination);
		Assert.Equal(SshTunnelDefinition.DefaultSocksPort, SshTunnelDefinition.Create(SshTunnelKind.Dynamic).ListenPort);
	}

	[Theory]
	[InlineData("localhost", true)]
	[InlineData("10.0.0.5", true)]
	[InlineData("db.internal.example", true)]
	[InlineData("", false)]
	[InlineData("  ", false)]
	[InlineData("two words", false)]
	[InlineData("pipe|host", false)]
	public void IsValidHost_AcceptsAddresses(string host, bool expected) =>
		Assert.Equal(expected, SshTunnelDefinition.IsValidHost(host));

	[Fact]
	public void Validate_RejectsAPortZeroListenerOnTheServer()
	{
		SshTunnelDefinition remote = SshTunnelDefinition.Create(SshTunnelKind.Remote) with { ListenPort = 0, DestinationPort = 22 };

		Assert.NotNull(remote.Validate());
		Assert.Null((remote with { ListenPort = 2222 }).Validate());
	}

	[Fact]
	public void Validate_AllowsPortZeroForALocalListener()
	{
		SshTunnelDefinition local = SshTunnelDefinition.Create(SshTunnelKind.Local) with { ListenPort = 0, DestinationPort = 443 };

		Assert.Null(local.Validate());
	}

	[Fact]
	public void Validate_IgnoresTheDestinationOfADynamicTunnel()
	{
		SshTunnelDefinition dynamicTunnel = SshTunnelDefinition.Create(SshTunnelKind.Dynamic) with { ListenPort = 1080 };

		Assert.Null(dynamicTunnel.Validate());
	}

	[Fact]
	public void Parse_ReadsWhatFormatWrote()
	{
		SshTunnelDefinition tunnel = SshTunnelDefinition.Create(SshTunnelKind.Remote) with
		{
			Name = "Webhook | staging",
			ListenHost = "0.0.0.0",
			ListenPort = 9000,
			DestinationHost = "127.0.0.1",
			DestinationPort = 3000,
			OpenWithSession = false,
		};

		Assert.Equal(tunnel, SshTunnelDefinition.Parse(tunnel.Format()));
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("not-a-guid|Local|127.0.0.1|80|host|80|auto|")]
	[InlineData("11111111-1111-1111-1111-111111111111|Sideways|127.0.0.1|80|host|80|auto|")]
	[InlineData("11111111-1111-1111-1111-111111111111|Local|127.0.0.1|80")]
	public void Parse_ReturnsNullForValuesItCannotRead(string value) =>
		Assert.Null(SshTunnelDefinition.Parse(value));

	[Fact]
	public void Describe_ShowsTheEndpointsAndCallsPortZeroAuto()
	{
		SshTunnelDefinition local = SshTunnelDefinition.Create(SshTunnelKind.Local) with
		{
			ListenPort = 0,
			DestinationHost = "db",
			DestinationPort = 5432,
		};

		Assert.Equal("127.0.0.1:auto to db:5432", local.Describe());
		Assert.Equal("127.0.0.1:15432 to db:5432", (local with { ListenPort = 15432 }).Describe());
	}

	[Fact]
	public void Display_PrefersTheName()
	{
		SshTunnelDefinition tunnel = SshTunnelDefinition.Create(SshTunnelKind.Dynamic) with { ListenPort = 1080 };

		Assert.Equal("SOCKS on 127.0.0.1:1080", tunnel.Display);
		Assert.Equal("Proxy", (tunnel with { Name = "Proxy" }).Display);
	}

	[Fact]
	public void Status_ShowsThePortThatWasActuallyTaken()
	{
		SshTunnelDefinition tunnel = SshTunnelDefinition.Create(SshTunnelKind.Local) with
		{
			ListenPort = 0,
			DestinationHost = "db",
			DestinationPort = 5432,
		};

		SshTunnelStatus status = new() { Tunnel = tunnel, State = SshTunnelState.Open, BoundPort = 49321 };

		Assert.Equal("127.0.0.1:49321 to db:5432", status.Describe());
	}
}
