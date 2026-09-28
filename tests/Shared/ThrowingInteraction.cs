using Mokaterm.Abstractions.Interaction;

namespace Mokaterm.Tests.Shared;

/// <summary>For code that must not prompt: every prompt fails the test. Notices are dropped, since they ask nothing.</summary>
internal sealed class ThrowingInteraction : IUserInteraction
{
	public Task<HostTrustDecision> ConfirmHostIdentityAsync(HostTrustPrompt prompt, CancellationToken cancellationToken = default) =>
		throw new InvalidOperationException("Unexpected host identity prompt.");

	public Task<CredentialPromptResult?> PromptCredentialsAsync(CredentialPrompt prompt, CancellationToken cancellationToken = default) =>
		throw new InvalidOperationException("Unexpected credential prompt.");

	public Task<SecretPromptResult?> PromptSecretAsync(SecretPrompt prompt, CancellationToken cancellationToken = default) =>
		throw new InvalidOperationException("Unexpected secret prompt.");

	public Task<IReadOnlyList<string>?> PromptKeyboardInteractiveAsync(KeyboardInteractivePrompt prompt, CancellationToken cancellationToken = default) =>
		throw new InvalidOperationException("Unexpected keyboard-interactive prompt.");

	public Task<ConfirmResult> ConfirmAsync(ConfirmPrompt prompt, CancellationToken cancellationToken = default) =>
		throw new InvalidOperationException("Unexpected confirmation.");

	public Task<OverwriteDecision> ConfirmOverwriteAsync(OverwritePrompt prompt, CancellationToken cancellationToken = default) =>
		throw new InvalidOperationException("Unexpected overwrite prompt.");

	public void Notify(NoticeSeverity severity, string message, string? title = null)
	{
	}
}
