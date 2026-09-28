namespace Mokaterm.UI.Interaction;

/// <summary>A queued prompt. <see cref="InteractionHost"/> shows the head of the queue and completes it with the answer.</summary>
internal abstract class InteractionRequest
{
	public Guid Id { get; } = Guid.NewGuid();

	/// <summary>Completes the request with the value its caller treats as cancelled.</summary>
	public abstract void CompleteCancelled();

	/// <summary>Calls <paramref name="onCancelled"/> when <paramref name="cancellationToken"/> fires.</summary>
	public abstract void AttachCancellation(Action<InteractionRequest> onCancelled, CancellationToken cancellationToken);
}
