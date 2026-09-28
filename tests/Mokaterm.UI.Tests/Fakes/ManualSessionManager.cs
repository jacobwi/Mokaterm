using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.UI.Tests.Fakes;

/// <summary>A session manager whose open set a test drives by hand.</summary>
internal sealed class ManualSessionManager : ISessionManager
{
	private readonly List<ISessionHandle> _sessions = [];
	private readonly List<Guid> _opened = [];

	public event Action? SessionsChanged;

	public IReadOnlyList<ISessionHandle> Sessions => [.. _sessions];

	/// <summary>The connection ids <see cref="OpenAsync"/> was called with, in order.</summary>
	public IReadOnlyList<Guid> Opened => [.. _opened];

	public FakeSession Add(Guid? connectionId = null, ITerminalStream? terminal = null)
	{
		FakeSession session = new(connectionId) { Terminal = terminal };
		Add(session);
		return session;
	}

	public void Add(ISessionHandle session)
	{
		_sessions.Add(session);
		SessionsChanged?.Invoke();
	}

	public void Remove(Guid sessionId)
	{
		_sessions.RemoveAll(session => session.Id == sessionId);
		SessionsChanged?.Invoke();
	}

	public ISessionHandle? Find(Guid sessionId) => _sessions.Find(session => session.Id == sessionId);

	public Task<ISessionHandle> OpenAsync(Guid connectionId, SessionOpenOptions? options = null, CancellationToken cancellationToken = default)
	{
		_opened.Add(connectionId);
		return Task.FromResult<ISessionHandle>(Add(connectionId));
	}

	public ISessionHandle OpenTransient(HostProfile host, ConnectionProfile connection, SessionOpenOptions? options = null) =>
		throw new NotSupportedException();

	public Task ReconnectAsync(Guid sessionId, CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task AdoptAsync(Guid sessionId, Guid connectionId, CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task DisconnectAsync(Guid sessionId) => Task.CompletedTask;

	public Task CloseAsync(Guid sessionId)
	{
		Remove(sessionId);
		return Task.CompletedTask;
	}

	public Task CloseAllAsync()
	{
		_sessions.Clear();
		SessionsChanged?.Invoke();
		return Task.CompletedTask;
	}

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
