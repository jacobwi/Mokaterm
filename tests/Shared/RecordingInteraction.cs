using Mokaterm.Abstractions.Interaction;

namespace Mokaterm.Tests.Shared;

/// <summary>
/// A user who dismisses every prompt but keeps the notices and the questions, so a test can read what was reported and
/// what was asked. <see cref="ConfirmAnswer"/> is how a test says yes.
/// </summary>
internal sealed class RecordingInteraction : IUserInteraction
{
	private readonly List<Notice> _notices = [];
	private readonly List<ConfirmPrompt> _confirms = [];

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

	public IReadOnlyList<ConfirmPrompt> Confirms
	{
		get
		{
			lock (_confirms)
			{
				return [.. _confirms];
			}
		}
	}

	/// <summary>What every <see cref="ConfirmAsync"/> answers.</summary>
	public ConfirmResult ConfirmAnswer { get; set; } = ConfirmResult.No;

	public Task<HostTrustDecision> ConfirmHostIdentityAsync(HostTrustPrompt prompt, CancellationToken cancellationToken = default) =>
		Task.FromResult(HostTrustDecision.Reject);

	public Task<CredentialPromptResult?> PromptCredentialsAsync(CredentialPrompt prompt, CancellationToken cancellationToken = default) =>
		Task.FromResult<CredentialPromptResult?>(null);

	public Task<SecretPromptResult?> PromptSecretAsync(SecretPrompt prompt, CancellationToken cancellationToken = default) =>
		Task.FromResult<SecretPromptResult?>(null);

	public Task<IReadOnlyList<string>?> PromptKeyboardInteractiveAsync(KeyboardInteractivePrompt prompt, CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<string>?>(null);

	public Task<ConfirmResult> ConfirmAsync(ConfirmPrompt prompt, CancellationToken cancellationToken = default)
	{
		lock (_confirms)
		{
			_confirms.Add(prompt);
		}

		return Task.FromResult(ConfirmAnswer);
	}

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
