using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Modules.Ssh.Docker;
using Mokaterm.Modules.Ssh.Tunnels;

namespace Mokaterm.Modules.Ssh.Tests.Docker;

/// <summary>The login a container's shell tab opens: the session's own, once more, with docker exec in its shell's place.</summary>
public sealed class DockerShellTabTests
{
	private static readonly SshTunnelDefinition SavedTunnel = SshTunnelDefinition.Create(SshTunnelKind.Local);

	private static readonly ConnectionProfile Login = new()
	{
		Id = Guid.NewGuid(),
		HostId = Guid.NewGuid(),
		ProtocolId = "ssh",
		Port = 2222,
		Username = "deploy",
		AuthenticationMethod = AuthenticationMethod.PublicKey,
		CredentialId = Guid.NewGuid(),
		Label = "prod",
		Options = new SshConnectionOptions
		{
			StartupCommand = "tmux attach",
			JumpConnectionIds = [Guid.NewGuid()],
			Tunnels = [SavedTunnel],
		}.ApplyTo(ProtocolOptions.Empty),
	};

	[Fact]
	public void TheTab_SignsInAsTheLoginDoes_UnderANewIdNamedAfterTheContainer()
	{
		(ConnectionProfile connection, SessionOpenOptions options) = DockerShellTab.For(Login, "shop-web-1", "sh", elevated: false);

		Assert.NotEqual(Login.Id, connection.Id);
		Assert.Equal(Login.HostId, connection.HostId);
		Assert.Equal(Login.CredentialId, connection.CredentialId);
		Assert.Equal(Login.AuthenticationMethod, connection.AuthenticationMethod);
		Assert.Equal(Login.Username, connection.Username);
		Assert.Equal(Login.Port, connection.Port);
		Assert.Equal("shop-web-1", connection.Label);
		Assert.Equal("shop-web-1", options.Title);
		Assert.Null(options.ProtocolId);
	}

	[Fact]
	public void TheLoginShell_GivesWayToDockerExec_InsteadOfTheLoginsOwnStartupCommand()
	{
		(ConnectionProfile connection, _) = DockerShellTab.For(Login, "shop-web-1", "bash", elevated: false);

		Assert.Equal("exec docker exec -it shop-web-1 bash", SshConnectionOptions.FromOptions(connection.Options).StartupCommand);
	}

	[Fact]
	public void WhileTheManagerRunsAsRoot_TheTabGoesThroughSudo()
	{
		(ConnectionProfile connection, _) = DockerShellTab.For(Login, "db", "sh", elevated: true);

		Assert.Equal("exec sudo docker exec -it db sh", SshConnectionOptions.FromOptions(connection.Options).StartupCommand);
	}

	// The login's own tab holds its saved tunnels; the copy would only fail to bind the same ports. The route stays.
	[Fact]
	public void TheTab_KeepsTheJumpHosts_AndLeavesTheSavedTunnelsBehind()
	{
		(ConnectionProfile connection, _) = DockerShellTab.For(Login, "db", "sh", elevated: false);
		SshConnectionOptions options = SshConnectionOptions.FromOptions(connection.Options);

		Assert.Empty(options.Tunnels);
		Assert.Equal(SshConnectionOptions.FromOptions(Login.Options).JumpConnectionIds, options.JumpConnectionIds);
		Assert.Single(SshConnectionOptions.FromOptions(Login.Options).Tunnels);
	}

	[Fact]
	public void AContainerNameDockerWouldRefuse_IsRefusedHere()
	{
		Assert.ThrowsAny<ArgumentException>(() => DockerShellTab.For(Login, "web; rm -rf /", "sh", elevated: false));
	}
}
