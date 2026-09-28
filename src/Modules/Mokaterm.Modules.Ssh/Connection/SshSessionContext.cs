using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;

namespace Mokaterm.Modules.Ssh.Connection;

/// <summary>
/// What a live session keeps from its connect: where it went, a copy of the login and the host key the user trusted, so
/// companion connections (the SFTP side panel, sudo for SFTP sessions) log in again without asking anything. A session
/// that reached its target through jump hosts also owns the chain, and companions dial the same loopback port.
/// </summary>
internal sealed class SshSessionContext : IAsyncDisposable
{
	public required Guid SessionId { get; init; }

	/// <summary>The target's own address, used for host key trust and for everything the user reads.</summary>
	public required string Host { get; init; }

	public required int Port { get; init; }

	public required IUserInteraction Interaction { get; init; }

	public required SshConnectionOptions Options { get; init; }

	/// <summary>Owned: wiped when the session closes.</summary>
	public required LoginCredentials Credentials { get; init; }

	/// <summary>The only host key companion connections accept.</summary>
	public required string HostKeyFingerprint { get; init; }

	/// <summary>Owned: the jump hosts this session goes through, or null when it dials its target directly.</summary>
	public SshJumpChain? Jump { get; init; }

	/// <summary>Where a socket actually goes: the loopback port of the last jump host, or the target itself.</summary>
	public SshEndpoint Dial => Jump?.Endpoint ?? new SshEndpoint(Host, Port);

	/// <summary><c>user@host</c>, for prompts and messages.</summary>
	public string Account => $"{Credentials.Username}@{Host}";

	public async ValueTask DisposeAsync()
	{
		Credentials.Dispose();
		if (Jump is { } jump)
		{
			await jump.DisposeAsync();
		}
	}
}
