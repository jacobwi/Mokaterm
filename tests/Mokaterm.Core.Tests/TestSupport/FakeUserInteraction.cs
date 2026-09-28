using Mokaterm.Abstractions.Interaction;

namespace Mokaterm.Core.Tests.TestSupport;

/// <summary>Answers host trust prompts with <see cref="HostTrustDecision"/> and records prompts and notices.</summary>
internal sealed class FakeUserInteraction : IUserInteraction
{
	private readonly Lock _sync = new();
	private readonly List<HostTrustPrompt> _hostTrustPrompts = [];
	private readonly List<Notice> _notices = [];
	private readonly TaskCompletionSource _promptShown = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public HostTrustDecision HostTrustDecision { get; set; } = HostTrustDecision.Reject;

	/// <summary>When set, host trust prompts stay open until it completes, like a dialog the user has not answered yet.</summary>
	public Task? HostTrustGate { get; set; }

	/// <summary>Completes when the first host trust prompt opens.</summary>
	public Task HostTrustPromptShown => _promptShown.Task;

	public IReadOnlyList<HostTrustPrompt> HostTrustPrompts
	{
		get
		{
			lock (_sync)
			{
				return [.. _hostTrustPrompts];
			}
		}
	}

	public IReadOnlyList<Notice> Notices
	{
		get
		{
			lock (_sync)
			{
				return [.. _notices];
			}
		}
	}

	public async Task<HostTrustDecision> ConfirmHostIdentityAsync(HostTrustPrompt prompt, CancellationToken cancellationToken = default)
	{
		lock (_sync)
		{
			_hostTrustPrompts.Add(prompt);
		}

		_promptShown.TrySetResult();
		if (HostTrustGate is { } gate)
		{
			await gate.WaitAsync(cancellationToken);
		}

		return HostTrustDecision;
	}

	public Task<CredentialPromptResult?> PromptCredentialsAsync(CredentialPrompt prompt, CancellationToken cancellationToken = default) =>
		Task.FromResult<CredentialPromptResult?>(null);

	public Task<SecretPromptResult?> PromptSecretAsync(SecretPrompt prompt, CancellationToken cancellationToken = default) =>
		Task.FromResult<SecretPromptResult?>(null);

	public Task<IReadOnlyList<string>?> PromptKeyboardInteractiveAsync(KeyboardInteractivePrompt prompt, CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<string>?>(null);

	public Task<ConfirmResult> ConfirmAsync(ConfirmPrompt prompt, CancellationToken cancellationToken = default) => Task.FromResult(ConfirmResult.No);

	public Task<OverwriteDecision> ConfirmOverwriteAsync(OverwritePrompt prompt, CancellationToken cancellationToken = default) =>
		Task.FromResult(OverwriteDecision.Cancel);

	public void Notify(NoticeSeverity severity, string message, string? title = null)
	{
		lock (_sync)
		{
			_notices.Add(new Notice(severity, message, title));
		}
	}
}
