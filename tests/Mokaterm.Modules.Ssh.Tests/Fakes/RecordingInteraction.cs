using Mokaterm.Abstractions.Interaction;

namespace Mokaterm.Modules.Ssh.Tests.Fakes;

/// <summary>A user who dismisses every prompt but keeps the notices, so a test can read what was reported.</summary>
internal sealed class RecordingInteraction : IUserInteraction
{
	private readonly List<Notice> _notices = [];

	public IReadOnlyList<Notice> Notices
	{
		get
		{
			lock (_notices)
			{
				return [.. _notices];
			}
		}
	}

	public Task<HostTrustDecision> ConfirmHostIdentityAsync(HostTrustPrompt prompt, CancellationToken cancellationToken = default) =>
		Task.FromResult(HostTrustDecision.Reject);

	public Task<CredentialPromptResult?> PromptCredentialsAsync(CredentialPrompt prompt, CancellationToken cancellationToken = default) =>
		Task.FromResult<CredentialPromptResult?>(null);

	public Task<SecretPromptResult?> PromptSecretAsync(SecretPrompt prompt, CancellationToken cancellationToken = default) =>
		Task.FromResult<SecretPromptResult?>(null);

	public Task<IReadOnlyList<string>?> PromptKeyboardInteractiveAsync(KeyboardInteractivePrompt prompt, CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<string>?>(null);

	public Task<ConfirmResult> ConfirmAsync(ConfirmPrompt prompt, CancellationToken cancellationToken = default) =>
		Task.FromResult(ConfirmResult.No);

	public Task<OverwriteDecision> ConfirmOverwriteAsync(OverwritePrompt prompt, CancellationToken cancellationToken = default) =>
		Task.FromResult(OverwriteDecision.Cancel);

	public void Notify(NoticeSeverity severity, string message, string? title = null)
	{
		lock (_notices)
		{
			_notices.Add(new Notice(severity, message, title));
		}
	}
}
