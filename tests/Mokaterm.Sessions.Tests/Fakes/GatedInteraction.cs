using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Sessions.Tests.Fakes;

/// <summary>A window where the user takes their time over the login prompt.</summary>
internal sealed class GatedInteraction : IUserInteraction
{
	private readonly TaskCompletionSource<CredentialPromptResult?> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private int _asked;

	public bool Asked => Volatile.Read(ref _asked) > 0;

	public void Answer(string username, string password) => _answer.SetResult(new CredentialPromptResult(username, password, Save: false));

	public Task<CredentialPromptResult?> PromptCredentialsAsync(CredentialPrompt prompt, CancellationToken cancellationToken = default)
	{
		_ = Interlocked.Increment(ref _asked);
		return _answer.Task.WaitAsync(cancellationToken);
	}

	public Task<HostTrustDecision> ConfirmHostIdentityAsync(HostTrustPrompt prompt, CancellationToken cancellationToken = default) =>
		Task.FromResult(HostTrustDecision.AcceptOnce);

	public Task<SecretPromptResult?> PromptSecretAsync(SecretPrompt prompt, CancellationToken cancellationToken = default) =>
		Task.FromResult<SecretPromptResult?>(null);

	public Task<IReadOnlyList<string>?> PromptKeyboardInteractiveAsync(KeyboardInteractivePrompt prompt, CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<string>?>(null);

	public Task<ConfirmResult> ConfirmAsync(ConfirmPrompt prompt, CancellationToken cancellationToken = default) => Task.FromResult(ConfirmResult.No);

	public Task<OverwriteDecision> ConfirmOverwriteAsync(OverwritePrompt prompt, CancellationToken cancellationToken = default) =>
		Task.FromResult(OverwriteDecision.Cancel);

	public void Notify(NoticeSeverity severity, string message, string? title = null)
	{
	}
}
