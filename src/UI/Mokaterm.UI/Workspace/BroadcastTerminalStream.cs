using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.UI.Workspace;

/// <summary>
/// A session's terminal stream with the broadcast tap in front of it: what the view sends goes to this session and,
/// while it types with others, to theirs as well. Everything else passes straight through.
/// </summary>
internal sealed class BroadcastTerminalStream : ITerminalStream
{
	private readonly ITerminalStream _inner;
	private readonly Guid _sessionId;
	private readonly InputBroadcast _broadcast;

	public BroadcastTerminalStream(ITerminalStream inner, Guid sessionId, InputBroadcast broadcast)
	{
		_inner = inner;
		_sessionId = sessionId;
		_broadcast = broadcast;
	}

	public event Action? StateChanged
	{
		add => _inner.StateChanged += value;
		remove => _inner.StateChanged -= value;
	}

	public TerminalSize Size => _inner.Size;

	public bool IsOpen => _inner.IsOpen;

	/// <summary>Whether this wraps <paramref name="stream"/>, so a view keeps one tap per session.</summary>
	public bool Wraps(ITerminalStream stream) => ReferenceEquals(_inner, stream);

	public IDisposable Attach(ITerminalSink sink) => _inner.Attach(sink);

	public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
	{
		await _inner.SendAsync(data, cancellationToken);
		foreach (ITerminalStream other in _broadcast.OthersFor(_sessionId))
		{
			await other.SendAsync(data, cancellationToken);
		}
	}

	public async ValueTask SendTextAsync(string text, CancellationToken cancellationToken = default)
	{
		await _inner.SendTextAsync(text, cancellationToken);
		foreach (ITerminalStream other in _broadcast.OthersFor(_sessionId))
		{
			await other.SendTextAsync(text, cancellationToken);
		}
	}

	/// <summary>Not repeated: a resize belongs to the window showing this session, not to the others.</summary>
	public ValueTask ResizeAsync(TerminalSize size, CancellationToken cancellationToken = default) =>
		_inner.ResizeAsync(size, cancellationToken);

	/// <summary>Not repeated: a local notice belongs in the tab that raised it.</summary>
	public ValueTask WriteLocalAsync(string text, CancellationToken cancellationToken = default) =>
		_inner.WriteLocalAsync(text, cancellationToken);
}
