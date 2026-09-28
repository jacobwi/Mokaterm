namespace Mokaterm.UI.Common.Interop;

/// <summary>
/// The envelope around one call into the page for the sinks that push a session at a view: terminal output, VNC bytes,
/// RDP frames. A call resolves only once the page has taken the work, which is what makes a busy or far away view slow
/// the session down instead of queueing it in memory, so the same six rules have to hold for every one of them: the
/// caller's token and this sink's detach are linked, a completion that never arrives is capped, the call starts on the
/// renderer's dispatcher, what crosses is already copied, only the expected teardown is swallowed, and nothing is left
/// faulted behind a caller that stopped waiting.
/// </summary>
public sealed class PageCalls : IDisposable
{
	// A completion that never arrives (a message lost while a Blazor Server circuit reconnects) must not stall the
	// session's pump for good; after this long the call counts as done and backpressure resumes with the next one.
	private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);

	private readonly Func<Func<Task>, Task> _dispatch;
	private readonly CancellationTokenSource _detached = new();
	private int _disposed;

	/// <param name="dispatch">Runs work on the renderer's dispatcher, which for a component is <c>InvokeAsync</c>.</param>
	public PageCalls(Func<Func<Task>, Task> dispatch)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		_dispatch = dispatch;
	}

	/// <summary>True once the sink was disposed, so a caller can drop work without copying a buffer for it first.</summary>
	public bool IsDetached => _detached.IsCancellationRequested;

	/// <summary>
	/// Runs one call into the page. Buffers the caller owns are copied before they are handed to <paramref name="call"/>,
	/// never inside it: past the cap the call runs on while the caller is already reusing its buffer.
	/// </summary>
	public ValueTask CallAsync(Func<CancellationToken, ValueTask> call, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(call);
		return RunAsync<object?>(null, (_, token) => call(token), cancellationToken);
	}

	/// <summary>
	/// Runs one call that needs what <paramref name="ready"/> completes with, the instance id a view only has once the
	/// page created it. The wait shares the call's token, so a detach or the cap releases it, and it happens before the
	/// hop so a view that never appears queues nothing on the dispatcher.
	/// </summary>
	public ValueTask CallAsync<T>(Task<T> ready, Func<T, CancellationToken, ValueTask> call, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(ready);
		ArgumentNullException.ThrowIfNull(call);
		return RunAsync(ready, call, cancellationToken);
	}

	/// <summary>Releases a call that is still waiting on the page, for example when the view goes away.</summary>
	public void Dispose()
	{
		// A view's teardown can reach this twice, and cancelling a disposed source would throw out of it.
		if (Interlocked.Exchange(ref _disposed, 1) == 0)
		{
			_detached.Cancel();
			_detached.Dispose();
		}
	}

	private async ValueTask RunAsync<T>(Task<T>? ready, Func<T, CancellationToken, ValueTask> call, CancellationToken cancellationToken)
	{
		if (_detached.IsCancellationRequested)
		{
			return;
		}

		try
		{
			using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _detached.Token);
			linked.CancelAfter(CallTimeout);
			CancellationToken token = linked.Token;
			T value = ready is null ? default! : await ready.WaitAsync(token);

			// JSRuntime numbers byte[] arguments with a counter that is not thread safe, and the session's pumps run on
			// their own threads, so the call starts on the renderer's dispatcher like the rest of the scope's interop.
			// Waiting with the token keeps a detach from blocking behind work queued on that dispatcher.
			await _dispatch(() => InvokeAsync(call, value, token)).WaitAsync(token);
		}
		catch (Exception ex) when (JsModule.IsExpected(ex) && !cancellationToken.IsCancellationRequested)
		{
			// Detached, timed out, or the view and its circuit are gone: the session carries on without this view.
		}
	}

	// Also runs after RunAsync stopped waiting, so it must not leave a faulted task nobody observes.
	private static async Task InvokeAsync<T>(Func<T, CancellationToken, ValueTask> call, T value, CancellationToken cancellationToken)
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return;
		}

		try
		{
			await call(value, cancellationToken);
		}
		catch (Exception ex) when (JsModule.IsExpected(ex))
		{
			// Reported through RunAsync when it is still waiting; nothing to do otherwise.
		}
	}
}
