using Mokaterm.Abstractions.Interaction;

namespace Mokaterm.UI.Interaction;

/// <summary>
/// <see cref="IUserInteraction"/> backed by a FIFO queue that <see cref="InteractionHost"/> shows one dialog at a time.
/// Safe to call from any thread. Notices wait in a small buffer until the host shows them, which also keeps them off
/// screen while the vault is locked.
/// </summary>
internal sealed class UserInteractionService : IUserInteraction, IDisposable
{
	// Notices raised while nothing can show them are capped, so a burst of connection errors cannot pile up unbounded.
	private const int MaxPendingNotices = 20;

	private readonly Lock _gate = new();
	private readonly List<InteractionRequest> _queue = [];
	private readonly Queue<PendingNotice> _notices = new();
	private bool _disposed;

	/// <summary>Raised when a request or notice is added or a request leaves the queue. Any thread.</summary>
	public event Action? Changed;

	/// <summary>The request to show now, or null.</summary>
	public InteractionRequest? Current
	{
		get
		{
			lock (_gate)
			{
				return _queue.Count > 0 ? _queue[0] : null;
			}
		}
	}

	public Task<HostTrustDecision> ConfirmHostIdentityAsync(HostTrustPrompt prompt, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(prompt);
		return EnqueueAsync(new HostTrustRequest(prompt), cancellationToken);
	}

	public Task<CredentialPromptResult?> PromptCredentialsAsync(CredentialPrompt prompt, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(prompt);
		return EnqueueAsync(new CredentialsRequest(prompt), cancellationToken);
	}

	public Task<SecretPromptResult?> PromptSecretAsync(SecretPrompt prompt, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(prompt);
		return EnqueueAsync(new SecretRequest(prompt), cancellationToken);
	}

	public Task<IReadOnlyList<string>?> PromptKeyboardInteractiveAsync(KeyboardInteractivePrompt prompt, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(prompt);
		return EnqueueAsync(new KeyboardInteractiveRequest(prompt), cancellationToken);
	}

	public Task<ConfirmResult> ConfirmAsync(ConfirmPrompt prompt, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(prompt);
		return EnqueueAsync(new ConfirmRequest(prompt), cancellationToken);
	}

	public Task<OverwriteDecision> ConfirmOverwriteAsync(OverwritePrompt prompt, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(prompt);
		return EnqueueAsync(new OverwriteRequest(prompt), cancellationToken);
	}

	public void Notify(NoticeSeverity severity, string message, string? title = null)
	{
		ArgumentNullException.ThrowIfNull(message);
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			if (_notices.Count == MaxPendingNotices)
			{
				_notices.Dequeue();
			}

			_notices.Enqueue(new PendingNotice(severity, message, title));
		}

		Changed?.Invoke();
	}

	/// <summary>Removes and returns the notices waiting to be shown.</summary>
	public IReadOnlyList<PendingNotice> TakeNotices()
	{
		lock (_gate)
		{
			if (_notices.Count == 0)
			{
				return [];
			}

			PendingNotice[] notices = [.. _notices];
			_notices.Clear();
			return notices;
		}
	}

	/// <summary>Answers <paramref name="request"/> and moves on to the next one.</summary>
	public void Complete<TResult>(InteractionRequest<TResult> request, TResult result)
	{
		ArgumentNullException.ThrowIfNull(request);
		bool removed;
		lock (_gate)
		{
			removed = _queue.Remove(request);
		}

		request.Complete(result);
		if (removed)
		{
			Changed?.Invoke();
		}
	}

	/// <summary>Completes <paramref name="request"/> with its cancelled value and removes it from the queue.</summary>
	public void Cancel(InteractionRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		bool removed;
		lock (_gate)
		{
			removed = _queue.Remove(request);
		}

		request.CompleteCancelled();
		if (removed)
		{
			Changed?.Invoke();
		}
	}

	public void Dispose()
	{
		List<InteractionRequest> pending;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			pending = [.. _queue];
			_queue.Clear();
			_notices.Clear();
		}

		foreach (InteractionRequest request in pending)
		{
			request.CompleteCancelled();
		}
	}

	private Task<TResult> EnqueueAsync<TResult>(InteractionRequest<TResult> request, CancellationToken cancellationToken)
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return Task.FromResult(request.CancelledValue);
		}

		lock (_gate)
		{
			if (_disposed)
			{
				return Task.FromResult(request.CancelledValue);
			}

			_queue.Add(request);
		}

		request.AttachCancellation(Cancel, cancellationToken);
		Changed?.Invoke();
		return request.Completion;
	}
}
