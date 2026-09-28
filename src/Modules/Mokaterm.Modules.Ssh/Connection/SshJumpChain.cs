using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Modules.Ssh.Sessions;
using Renci.SshNet;

namespace Mokaterm.Modules.Ssh.Connection;

/// <summary>
/// The hops a session takes to reach a target it cannot dial directly. SSH.NET has no ProxyJump, so each hop is a real
/// SSH login that opens a loopback <see cref="ForwardedPortLocal"/> to the next one; the session then connects to that
/// port and does its own key exchange and login end to end, exactly as OpenSSH's <c>-J</c> does.
/// </summary>
/// <remarks>
/// The chain lives as long as the session: closing it drops every hop. Each hop verifies its own host key and asks for
/// its own credentials, because each one is a separate login.
/// </remarks>
internal sealed class SshJumpChain : IAsyncDisposable
{
	private const string LoopbackHost = "127.0.0.1";

	private readonly List<Hop> _hops = [];
	private int _disposed;

	private SshJumpChain()
	{
	}

	/// <summary>Where the session dials to reach its target: a loopback port held open by the last hop.</summary>
	public SshEndpoint Endpoint { get; private set; } = new(LoopbackHost, 0);

	/// <summary>
	/// Logs in to every hop and returns the chain, which the caller owns. Anything that fails part way closes the hops
	/// that were already up before it throws.
	/// </summary>
	/// <exception cref="ProtocolConnectException">A hop is missing, could not be reached or refused the login.</exception>
	public static async Task<SshJumpChain> OpenAsync(
		SshConnector connector,
		IConnectionResolver resolver,
		ProtocolConnectContext context,
		IReadOnlyList<Guid> jumpConnectionIds,
		SshEndpoint target,
		SshJumpTrail trail,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(connector);
		ArgumentNullException.ThrowIfNull(resolver);
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(jumpConnectionIds);
		ArgumentNullException.ThrowIfNull(target);
		ArgumentNullException.ThrowIfNull(trail);

		SshJumpChain chain = new();
		List<ResolvedConnection> resolved = [];
		try
		{
			foreach (Guid connectionId in jumpConnectionIds)
			{
				if (!trail.TryEnter(connectionId))
				{
					throw new ProtocolConnectException(
						ConnectFailure.ProtocolError,
						$"The jump hosts of this connection lead back to a login already in the chain, or the chain is longer than {SshJumpTrail.MaxHops} hops.");
				}

				resolved.Add(await resolver.ResolveAsync(connectionId, cancellationToken)
					?? throw new ProtocolConnectException(
						ConnectFailure.AuthenticationFailed,
						"A jump host of this connection no longer exists. Edit the connection and pick another."));
			}

			for (int index = 0; index < resolved.Count; index++)
			{
				ResolvedConnection hop = resolved[index];
				SshEndpoint next = index + 1 < resolved.Count ? AddressOf(resolved[index + 1]) : target;

				// The first hop is dialled directly; every later one goes through the port the hop before it holds open.
				SshEndpoint? dial = index == 0 ? null : chain.Endpoint;
				await chain.AddHopAsync(connector, context, hop, dial, next, trail, cancellationToken);
			}

			return chain;
		}
		catch
		{
			await chain.DisposeAsync();
			throw;
		}
		finally
		{
			foreach (ResolvedConnection hop in resolved)
			{
				hop.Dispose();
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		// Newest hop first, so a hop is never left forwarding into a client that is already gone.
		for (int index = _hops.Count - 1; index >= 0; index--)
		{
			await _hops[index].DisposeAsync();
		}

		_hops.Clear();
	}

	private static SshEndpoint AddressOf(ResolvedConnection connection) =>
		new(connection.Host.Address, connection.GetPort(Protocols.SshProtocolProvider.SshDescriptor.DefaultPort));

	private async Task AddHopAsync(
		SshConnector connector,
		ProtocolConnectContext context,
		ResolvedConnection hop,
		SshEndpoint? dial,
		SshEndpoint next,
		SshJumpTrail trail,
		CancellationToken cancellationToken)
	{
		SshEndpoint identity = AddressOf(hop);
		string account = string.IsNullOrWhiteSpace(hop.Connection.Username) ? identity.Host : $"{hop.Connection.Username}@{identity.Host}";
		context.Status?.Report($"Connecting to the jump host {account}");

		ProtocolConnectContext hopContext = new()
		{
			SessionId = context.SessionId,
			Host = hop.Host,
			Connection = hop.Connection,
			Port = identity.Port,
			Credentials = hop.Credentials,
			HostVerifier = context.HostVerifier,
			Interaction = context.Interaction,
			Status = context.Status,
			Resolver = context.Resolver,
		};

		// A hop reached through an earlier hop is already tunnelled, so the connector skips its own jump hosts: only
		// the first hop, which is dialled directly, may have a chain of its own.
		SshConnection<SshClient> connection = await connector.ConnectAsync(
			hopContext,
			static info => new SshClient(info),
			dial,
			trail,
			cancellationToken);

		Hop created = new(connection, account);
		_hops.Add(created);
		Endpoint = await created.ForwardAsync(next, cancellationToken);
	}

	/// <summary>One hop: a live client and the loopback port it holds open to the next address.</summary>
	private sealed class Hop : IAsyncDisposable
	{
		private readonly SshConnection<SshClient> _connection;
		private ForwardedPortLocal? _port;

		public Hop(SshConnection<SshClient> connection, string account)
		{
			_connection = connection;
			Description = $"{account}:{connection.Context.Port}";
		}

		public string Description { get; }

		/// <summary>Binds a loopback port that this hop forwards to <paramref name="next"/>.</summary>
		/// <exception cref="ProtocolConnectException">The port could not be opened.</exception>
		public async Task<SshEndpoint> ForwardAsync(SshEndpoint next, CancellationToken cancellationToken)
		{
			ForwardedPortLocal port = new(LoopbackHost, 0, next.Host, (uint)next.Port);
			try
			{
				_connection.Client.AddForwardedPort(port);

				// Start binds the listening socket, which is blocking work.
				await Task.Run(port.Start, cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				port.Dispose();
				throw new ProtocolConnectException(
					ConnectFailure.ProtocolError,
					$"The jump host {Description} could not open a tunnel to {next.Host}:{next.Port}: {ex.Message}",
					ex);
			}

			_port = port;

			// SSH.NET writes the port the operating system picked back into BoundPort once the socket is bound.
			return new SshEndpoint(LoopbackHost, (int)port.BoundPort);
		}

		public async ValueTask DisposeAsync()
		{
			if (_port is { } port)
			{
				_port = null;
				try
				{
					await Task.Run(() =>
					{
						port.Stop();
						port.Dispose();
					});
				}
				catch (Exception)
				{
					// The client below it is about to go away, which closes the port anyway.
				}
			}

			await CompanionConnection<SshClient>.DisposeClientAsync(_connection.Client);
			await _connection.Context.DisposeAsync();
		}
	}
}
