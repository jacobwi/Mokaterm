using Mokaterm.Abstractions.Interaction;

namespace Mokaterm.Tests.Shared;

/// <summary>A user who dismisses every prompt, so a test fails instead of hanging when something asks.</summary>
internal sealed class DismissingInteraction : IUserInteraction
{
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
	}
}
