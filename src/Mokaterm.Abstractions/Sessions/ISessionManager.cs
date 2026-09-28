using Mokaterm.Abstractions.Connections;

namespace Mokaterm.Abstractions.Sessions;

/// <summary>
/// Open sessions for one UI scope (a MAUI window or a Blazor Server circuit). Disposing the manager closes
/// every session, so connections never outlive the UI that owns them.
/// </summary>
public interface ISessionManager : IAsyncDisposable
{
	/// <summary>Sessions in the order they were opened.</summary>
	IReadOnlyList<ISessionHandle> Sessions { get; }

	/// <summary>Raised when a session is added or removed. Per-session changes raise <see cref="ISessionHandle.Changed"/>.</summary>
	event Action? SessionsChanged;

	ISessionHandle? Find(Guid sessionId);

	/// <summary>
	/// Opens a saved connection. Returns as soon as the handle exists; connecting continues in the background
	/// and reports through the handle.
	/// </summary>
	/// <exception cref="KeyNotFoundException">No connection with that id.</exception>
	Task<ISessionHandle> OpenAsync(Guid connectionId, SessionOpenOptions? options = null, CancellationToken cancellationToken = default);

	/// <summary>Opens a connection that is not saved, such as one typed into quick connect.</summary>
	ISessionHandle OpenTransient(HostProfile host, ConnectionProfile connection, SessionOpenOptions? options = null);

	/// <summary>Reconnects a disconnected or failed session in place. Its terminal stream carries over.</summary>
	Task ReconnectAsync(Guid sessionId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Turns a session opened with <see cref="OpenTransient"/> into a session of the saved login it was just saved as.
	/// Nothing happens when the session is already saved or no longer open.
	/// </summary>
	/// <exception cref="KeyNotFoundException">No connection with that id.</exception>
	Task AdoptAsync(Guid sessionId, Guid connectionId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Stops a session but keeps its tab: cancels a connect in progress, closes a live connection, or stops a pending
	/// automatic reconnect. The terminal scrollback stays, ready for <see cref="ReconnectAsync"/>.
	/// </summary>
	Task DisconnectAsync(Guid sessionId);

	/// <summary>Disconnects the session and removes its tab.</summary>
	Task CloseAsync(Guid sessionId);

	Task CloseAllAsync();
}

public sealed record SessionOpenOptions
{
	/// <summary>Open with a variant protocol, for example <c>sftp</c> for a saved SSH connection.</summary>
	public string? ProtocolId { get; init; }

	/// <summary>Replaces the generated tab title.</summary>
	public string? Title { get; init; }
}
