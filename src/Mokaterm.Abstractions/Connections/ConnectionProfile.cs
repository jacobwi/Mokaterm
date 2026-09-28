using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Abstractions.Connections;

/// <summary>
/// One way to log in to a host: protocol, port, user and credential. Secrets never live here, only a
/// reference to a vault credential.
/// </summary>
public sealed record ConnectionProfile
{
	public required Guid Id { get; init; }

	public required Guid HostId { get; init; }

	public required string ProtocolId { get; init; }

	/// <summary>Null means the protocol's default port.</summary>
	public int? Port { get; init; }

	public string? Username { get; init; }

	public AuthenticationMethod AuthenticationMethod { get; init; }

	/// <summary>
	/// Vault credential holding the password or private key. Null with <see cref="AuthenticationMethod.Password"/>
	/// means the user is asked at connect time.
	/// </summary>
	public Guid? CredentialId { get; init; }

	/// <summary>Overrides the generated <c>user@address</c> title.</summary>
	public string? Label { get; init; }

	public ProtocolOptions Options { get; init; } = ProtocolOptions.Empty;

	/// <summary>Terminal appearance for this connection only, on top of the global terminal settings.</summary>
	public TerminalProfileOverrides? Terminal { get; init; }

	public bool IsFavorite { get; init; }

	public DateTimeOffset CreatedAt { get; init; }

	public DateTimeOffset UpdatedAt { get; init; }

	public DateTimeOffset? LastConnectedAt { get; init; }

	/// <summary><see cref="Label"/> when set, otherwise <c>user@address</c> or just the address.</summary>
	public string GetTitle(HostProfile host) =>
		!string.IsNullOrWhiteSpace(Label) ? Label
		: string.IsNullOrWhiteSpace(Username) ? host.Address
		: $"{Username}@{host.Address}";
}
