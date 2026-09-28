using Mokaterm.Abstractions.Terminal;
using Mokaterm.UI.Common.Interop;

namespace Mokaterm.UI.Terminal.Interop;

/// <summary>
/// Feeds stream output into one xterm.js instance. A write completes only after xterm.js parsed it, so a busy view
/// slows the pump (and through it the remote side) instead of queueing output without limit.
/// </summary>
internal sealed class TerminalOutputSink : ITerminalSink, IDisposable
{
	private readonly TerminalInterop _interop;
	private readonly Task<int> _instance;
	private readonly PageCalls _calls;

	/// <param name="interop">The scope's terminal.js wrapper.</param>
	/// <param name="instance">Completes with the xterm.js instance id; output that arrives earlier waits for it.</param>
	/// <param name="dispatch">Runs work on the renderer's dispatcher.</param>
	public TerminalOutputSink(TerminalInterop interop, Task<int> instance, Func<Func<Task>, Task> dispatch)
	{
		_interop = interop;
		_instance = instance;
		_calls = new PageCalls(dispatch);
	}

	public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
	{
		if (data.IsEmpty || _calls.IsDetached)
		{
			return ValueTask.CompletedTask;
		}

		// The pump reuses its buffer, so the chunk is copied before it crosses into JavaScript.
		byte[] chunk = data.ToArray();
		return _calls.CallAsync(_instance, (id, token) => _interop.WriteAsync(id, chunk, token), cancellationToken);
	}

	/// <summary>Releases a write that is still waiting on xterm.js, for example when the view goes away.</summary>
	public void Dispose() => _calls.Dispose();
}
