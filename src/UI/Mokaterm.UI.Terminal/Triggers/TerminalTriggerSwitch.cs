using System.Runtime.CompilerServices;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.UI.Terminal.Triggers;

/// <summary>
/// The sessions whose triggers are paused, kept by stream rather than by view: a tab moved to the other pane gets a new
/// view of the same stream, and the choice goes with it. Scoped like the sessions, and weak, so a closed session's stream
/// is not kept alive by having been paused once.
/// </summary>
internal sealed class TerminalTriggerSwitch
{
	private static readonly object Paused = new();

	private readonly ConditionalWeakTable<ITerminalStream, object> _paused = [];

	public bool IsPaused(ITerminalStream stream) => _paused.TryGetValue(stream, out _);

	public void SetPaused(ITerminalStream stream, bool paused)
	{
		if (paused)
		{
			_paused.AddOrUpdate(stream, Paused);
		}
		else
		{
			_ = _paused.Remove(stream);
		}
	}
}
