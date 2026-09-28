using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ssh.Tunnels;

namespace Mokaterm.Modules.Ssh.Tests;

public sealed class SshTunnelOptionsTests
{
	private static readonly Guid FirstId = new("11111111-1111-1111-1111-111111111111");

	private static readonly Guid SecondId = new("22222222-2222-2222-2222-222222222222");

	[Fact]
	public void RoundTrip_KeepsEveryTunnelField()
	{
		SshConnectionOptions options = new()
		{
			Tunnels =
			[
				new SshTunnelDefinition
				{
					Id = FirstId,
					Kind = SshTunnelKind.Local,
					Name = "Database | replica",
					ListenHost = "127.0.0.1",
					ListenPort = 15432,
					DestinationHost = "db.internal",
					DestinationPort = 5432,
					OpenWithSession = true,
				},
				new SshTunnelDefinition
				{
					Id = SecondId,
					Kind = SshTunnelKind.Dynamic,
					ListenHost = "0.0.0.0",
					ListenPort = 1080,
					OpenWithSession = false,
				},
			],
		};

		SshConnectionOptions roundTripped = SshConnectionOptions.FromOptions(options.ApplyTo(ProtocolOptions.Empty));

		Assert.Equal(options, roundTripped);
		Assert.Equal("Database | replica", roundTripped.Tunnels[0].Name);
		Assert.False(roundTripped.Tunnels[1].OpenWithSession);
	}

	[Fact]
	public void ApplyTo_WritesOneNumberedKeyPerTunnel()
	{
		SshConnectionOptions options = new()
		{
			Tunnels = [Local(FirstId, 8080), Local(SecondId, 8081)],
		};

		ProtocolOptions stored = options.ApplyTo(ProtocolOptions.Empty);

		Assert.Equal(["ssh.tunnel.00", "ssh.tunnel.01"], stored.Keys);
		Assert.StartsWith(FirstId.ToString("D"), stored["ssh.tunnel.00"], StringComparison.Ordinal);
	}

	[Fact]
	public void ApplyTo_RemovingATunnelLeavesNoGap()
	{
		ProtocolOptions stored = new SshConnectionOptions { Tunnels = [Local(FirstId, 8080), Local(SecondId, 8081)] }
			.ApplyTo(ProtocolOptions.Empty);

		ProtocolOptions updated = new SshConnectionOptions { Tunnels = [Local(SecondId, 8081)] }.ApplyTo(stored);

		Assert.Equal(["ssh.tunnel.00"], updated.Keys);
		Assert.Contains(SecondId.ToString("D"), updated["ssh.tunnel.00"], StringComparison.Ordinal);
	}

	[Fact]
	public void ApplyTo_KeepsOtherModulesKeys()
	{
		ProtocolOptions existing = ProtocolOptions.Empty.With("other.module", "keep me").With("ssh.tunnel.07", "not a tunnel");

		ProtocolOptions updated = new SshConnectionOptions { Tunnels = [Local(FirstId, 80)] }.ApplyTo(existing);

		Assert.Equal("keep me", updated["other.module"]);
		Assert.DoesNotContain("ssh.tunnel.07", updated.Keys);
	}

	[Fact]
	public void FromOptions_DropsValuesThatDoNotParse()
	{
		ProtocolOptions stored = ProtocolOptions.Empty
			.With("ssh.tunnel.00", "not a tunnel at all")
			.With("ssh.tunnel.01", Local(SecondId, 22).Format());

		SshConnectionOptions options = SshConnectionOptions.FromOptions(stored);

		Assert.Equal(SecondId, Assert.Single(options.Tunnels).Id);
	}

	[Fact]
	public void FromOptions_DropsATunnelThatWouldNotOpen()
	{
		SshTunnelDefinition invalid = Local(FirstId, 8080) with { DestinationPort = 0 };
		ProtocolOptions stored = ProtocolOptions.Empty.With("ssh.tunnel.00", invalid.Format());

		Assert.Empty(SshConnectionOptions.FromOptions(stored).Tunnels);
	}

	[Fact]
	public void JumpHosts_RoundTripInOrder()
	{
		SshConnectionOptions options = new() { JumpConnectionIds = [SecondId, FirstId] };

		ProtocolOptions stored = options.ApplyTo(ProtocolOptions.Empty);
		SshConnectionOptions roundTripped = SshConnectionOptions.FromOptions(stored);

		Assert.Equal($"{SecondId:D},{FirstId:D}", stored["ssh.jumpHosts"]);
		Assert.Equal([SecondId, FirstId], roundTripped.JumpConnectionIds);
	}

	[Fact]
	public void JumpHosts_IgnoreDuplicatesAndValuesThatAreNotIds()
	{
		SshConnectionOptions options = SshConnectionOptions.FromOptions(
			ProtocolOptions.Empty.With("ssh.jumpHosts", $" {FirstId:D} , not-an-id ,{FirstId:D},{Guid.Empty:D},{SecondId:D}"));

		Assert.Equal([FirstId, SecondId], options.JumpConnectionIds);
	}

	[Fact]
	public void AgentIdentity_RoundTrips()
	{
		SshConnectionOptions options = new() { AgentIdentity = "SHA256:abc" };

		ProtocolOptions stored = options.ApplyTo(ProtocolOptions.Empty);

		Assert.Equal("SHA256:abc", stored["ssh.agentIdentity"]);
		Assert.Equal("SHA256:abc", SshConnectionOptions.FromOptions(stored).AgentIdentity);
	}

	[Fact]
	public void Empty_HasNoTunnelsAndNoJumpHosts()
	{
		SshConnectionOptions options = SshConnectionOptions.FromOptions(ProtocolOptions.Empty);

		Assert.Equal(new SshConnectionOptions(), options);
		Assert.Empty(options.Tunnels);
		Assert.Empty(options.JumpConnectionIds);
		Assert.Null(options.AgentIdentity);
	}

	private static SshTunnelDefinition Local(Guid id, int listenPort) => new()
	{
		Id = id,
		Kind = SshTunnelKind.Local,
		ListenHost = "127.0.0.1",
		ListenPort = listenPort,
		DestinationHost = "localhost",
		DestinationPort = 5432,
	};
}
