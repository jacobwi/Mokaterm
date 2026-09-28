namespace Mokaterm.UI.Interaction;

/// <summary>A queued prompt whose caller awaits a <typeparamref name="TResult"/>.</summary>
internal abstract class InteractionRequest<TResult> : InteractionRequest
{
	// Continuations run on the thread pool, never inline in the renderer's click handler that completes the request.
	private readonly TaskCompletionSource<TResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private CancellationTokenRegistration _registration;

	protected InteractionRequest(TResult cancelledValue) => CancelledValue = cancelledValue;

	public TResult CancelledValue { get; }

	public Task<TResult> Completion => _completion.Task;

	public void Complete(TResult result)
	{
		// Unregister rather than Dispose: this can run inside the token's own callback, where Dispose would wait for it.
		_registration.Unregister();
		_completion.TrySetResult(result);
	}

	public override void CompleteCancelled() => Complete(CancelledValue);

	public override void AttachCancellation(Action<InteractionRequest> onCancelled, CancellationToken cancellationToken)
	{
		if (cancellationToken.CanBeCanceled)
		{
			_registration = cancellationToken.Register(() => onCancelled(this));
		}
	}
}
