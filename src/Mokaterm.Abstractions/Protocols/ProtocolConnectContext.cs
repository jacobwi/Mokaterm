using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Security;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Abstractions.Protocols;

/// <summary>Everything a provider needs for one connection attempt.</summary>
public sealed class ProtocolConnectContext
{
	public required Guid SessionId { get; init; }

	public required HostProfile Host { get; init; }

	public required ConnectionProfile Connection { get; init; }

	/// <summary>The port to use: the connection's own port or the protocol default.</summary>
	public required int Port { get; init; }

	public required ICredentialSource Credentials { get; init; }

	public required IHostIdentityVerifier HostVerifier { get; init; }

	/// <summary>Prompts that belong to the connection, such as keyboard-interactive questions or a sudo password.</summary>
	public required IUserInteraction Interaction { get; init; }

	/// <summary>
	/// Opens another saved login, for protocols that hop through one (SSH jump hosts). Null when the caller has no
	/// connection catalog, as in tests and samples.
	/// </summary>
	public IConnectionResolver? Resolver { get; init; }

	/// <summary>Short progress messages such as "Authenticating".</summary>
	public IProgress<string>? Status { get; init; }

	/// <summary>Initial pseudo-terminal size for protocols with a terminal.</summary>
	public TerminalSize TerminalSize { get; init; } = TerminalSize.Default;
}
