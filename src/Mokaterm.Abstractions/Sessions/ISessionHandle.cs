using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Abstractions.Sessions;

public enum SessionState
{
	Connecting,
	Connected,
	Disconnected,
	Failed,
}

/// <summary>A session tab's view of one connection over its whole life, across reconnects.</summary>
public interface ISessionHandle
{
	Guid Id { get; }

	string Title { get; }

	HostProfile Host { get; }

	ConnectionProfile Connection { get; }

	/// <summary>The protocol actually used, which may be a variant of the saved connection's protocol.</summary>
	ProtocolDescriptor Protocol { get; }

	/// <summary>True for quick-connect sessions whose profile is not saved.</summary>
	bool IsTransient { get; }

	SessionState State { get; }

	/// <summary>Progress while connecting; the reason after a disconnect or failure.</summary>
	string? StatusMessage { get; }

	ConnectFailure? Failure { get; }

	DateTimeOffset OpenedAt { get; }

	DateTimeOffset? ConnectedAt { get; }

	/// <summary>Terminal output for protocols with a terminal. The same object across reconnects.</summary>
	ITerminalStream? Terminal { get; }

	/// <summary>The live protocol session. Null unless <see cref="State"/> is <see cref="SessionState.Connected"/>.</summary>
	IProtocolSession? Session { get; }

	/// <summary>Raised whenever any property changes. Handlers may run on any thread.</summary>
	event Action? Changed;

	/// <summary>
	/// True for the states <see cref="ISessionManager.ReconnectAsync"/> acts on, which is the one rule: the manager, the
	/// handle, the tabs and the file browser all ask here, so a reconnect the shell offers is one that happens.
	/// </summary>
	static bool CanReconnect(SessionState state) => state is SessionState.Disconnected or SessionState.Failed;
}
