using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Sessions.Commands;

/// <summary>One connect for <see cref="DetachedConnector"/>.</summary>
internal sealed record DetachedConnect
{
	public required Guid SessionId { get; init; }

	public required HostProfile Host { get; init; }

	public required ConnectionProfile Connection { get; init; }

	public required ProtocolDescriptor Protocol { get; init; }

	public required ConnectPurpose Purpose { get; init; }

	public required IUserInteraction Interaction { get; init; }

	public required IHostIdentityVerifier Verifier { get; init; }

	public IProgress<string>? Status { get; init; }
}
