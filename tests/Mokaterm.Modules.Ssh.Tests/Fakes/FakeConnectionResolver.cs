using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Modules.Ssh.Tests.Fakes;

/// <summary>Saved logins a jump chain can resolve, without a vault behind them.</summary>
internal sealed class FakeConnectionResolver : IConnectionResolver
{
	private readonly Dictionary<Guid, ResolvedConnection> _connections = [];
	private readonly List<Guid> _resolved = [];

	public IReadOnlyList<Guid> Resolved
	{
		get
		{
			lock (_resolved)
			{
				return [.. _resolved];
			}
		}
	}

	/// <summary>Adds a login at <paramref name="address"/> that logs in with a password.</summary>
	public Guid Add(string address, int port, string username = "tester", string password = "secret", ProtocolOptions? options = null)
	{
		Guid hostId = Guid.NewGuid();
		Guid connectionId = Guid.NewGuid();
		HostProfile host = new() { Id = hostId, Address = address };
		ConnectionProfile connection = new()
		{
			Id = connectionId,
			HostId = hostId,
			ProtocolId = "ssh",
			Port = port,
			Username = username,
			AuthenticationMethod = AuthenticationMethod.Password,
			Options = options ?? ProtocolOptions.Empty,
		};

		_connections[connectionId] = new ResolvedConnection(host, connection, new PasswordCredentialSource(username, password));
		return connectionId;
	}

	/// <summary>Points a saved login at jump hosts of its own, which is how a chain can be made to loop.</summary>
	public void SetJumpHosts(Guid connectionId, params Guid[] jumpHosts)
	{
		ResolvedConnection existing = _connections[connectionId];
		ConnectionProfile connection = existing.Connection with
		{
			Options = existing.Connection.Options.With(
				SshConnectionOptions.JumpHostsKey,
				string.Join(',', jumpHosts.Select(id => id.ToString("D")))),
		};

		_connections[connectionId] = new ResolvedConnection(existing.Host, connection, existing.Credentials);
	}

	public ValueTask<ResolvedConnection?> ResolveAsync(Guid connectionId, CancellationToken cancellationToken = default)
	{
		lock (_resolved)
		{
			_resolved.Add(connectionId);
		}

		return ValueTask.FromResult(_connections.GetValueOrDefault(connectionId));
	}
}
