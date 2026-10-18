using System.Runtime.CompilerServices;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.UI.Terminal.Triggers;

/// <summary>
/// The sessions whose triggers are paused, kept by session id where the view has one and by stream otherwise, never by
/// view: a tab moved to the other pane gets a new view and, in the shell, a new <c>BroadcastTerminalStream</c> in front of
/// the same session, and the choice goes with it. Scoped like the sessions. Streams are held weakly, so a closed session's
/// stream is not kept alive by having been paused once; an id is a few bytes for the window's life.
/// </summary>
internal sealed class TerminalTriggerSwitch
{
	private static readonly object Paused = new();

	private readonly ConditionalWeakTable<ITerminalStream, object> _pausedStreams = [];
	private readonly HashSet<Guid> _pausedSessions = [];

	public bool IsPaused(Guid? sessionId, ITerminalStream stream) => sessionId is { } id
		? _pausedSessions.Contains(id)
		: _pausedStreams.TryGetValue(stream, out _);

	public void SetPaused(Guid? sessionId, ITerminalStream stream, bool paused)
	{
		if (sessionId is { } id)
		{
			_ = paused ? _pausedSessions.Add(id) : _pausedSessions.Remove(id);
		}
		else if (paused)
		{
			_pausedStreams.AddOrUpdate(stream, Paused);
		}
		else
		{
			_ = _pausedStreams.Remove(stream);
		}
	}
}
