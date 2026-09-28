using Mokaterm.UI.Common.Interop;

namespace Mokaterm.Modules.Vnc.Interop;

/// <summary>
/// Feeds session bytes into one RFB instance. A write completes only after noVNC parsed it, so a busy view slows the
/// relay (and through it the server) instead of queueing screen updates without limit.
/// </summary>
internal sealed class VncPageSink : IVncSink, IDisposable
{
	private readonly VncInterop _interop;
	private readonly int _instanceId;
	private readonly PageCalls _calls;

	/// <param name="dispatch">Runs work on the renderer's dispatcher.</param>
	public VncPageSink(VncInterop interop, int instanceId, Func<Func<Task>, Task> dispatch)
	{
		_interop = interop;
		_instanceId = instanceId;
		_calls = new PageCalls(dispatch);
	}

	public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
	{
		if (data.IsEmpty || _calls.IsDetached)
		{
			return ValueTask.CompletedTask;
		}

		// The relay reuses its buffer, so the chunk is copied before it crosses into JavaScript.
		byte[] chunk = data.ToArray();
		return _calls.CallAsync(token => _interop.DeliverAsync(_instanceId, chunk, token), cancellationToken);
	}

	/// <summary>Releases a write still waiting on the page, for example when the view goes away.</summary>
	public void Dispose() => _calls.Dispose();
}
