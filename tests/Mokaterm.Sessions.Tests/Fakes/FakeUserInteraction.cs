using Mokaterm.Abstractions.Interaction;

namespace Mokaterm.Sessions.Tests.Fakes;

/// <summary>Answers credential prompts from a queue (an empty queue cancels) and records prompts and notices.</summary>
internal sealed class FakeUserInteraction : IUserInteraction
{
	private readonly Lock _lock = new();
	private readonly Queue<CredentialPromptResult?> _credentialAnswers = new();
	private readonly Queue<SecretPromptResult?> _secretAnswers = new();
	private readonly List<CredentialPrompt> _credentialPrompts = [];
	private readonly List<SecretPrompt> _secretPrompts = [];
	private readonly List<Notice> _notices = [];

	public IReadOnlyList<CredentialPrompt> CredentialPrompts
	{
		get
		{
			lock (_lock)
			{
				return [.. _credentialPrompts];
			}
		}
	}

	public IReadOnlyList<Notice> Notices
	{
		get
		{
			lock (_lock)
			{
				return [.. _notices];
			}
		}
	}

	public IReadOnlyList<SecretPrompt> SecretPrompts
	{
		get
		{
			lock (_lock)
			{
				return [.. _secretPrompts];
			}
		}
	}

	public void AnswerCredentials(string username, string password, bool save = false)
	{
		lock (_lock)
		{
			_credentialAnswers.Enqueue(new CredentialPromptResult(username, password, save));
		}
	}

	public void AnswerSecret(string secret, bool remember = false)
	{
		lock (_lock)
		{
			_secretAnswers.Enqueue(new SecretPromptResult(secret, remember));
		}
	}

	public Task<CredentialPromptResult?> PromptCredentialsAsync(CredentialPrompt prompt, CancellationToken cancellationToken = default)
	{
		lock (_lock)
		{
			_credentialPrompts.Add(prompt);
			return Task.FromResult(_credentialAnswers.TryDequeue(out CredentialPromptResult? answer) ? answer : null);
		}
	}

	public Task<HostTrustDecision> ConfirmHostIdentityAsync(HostTrustPrompt prompt, CancellationToken cancellationToken = default) =>
		Task.FromResult(HostTrustDecision.AcceptOnce);

	public Task<SecretPromptResult?> PromptSecretAsync(SecretPrompt prompt, CancellationToken cancellationToken = default)
	{
		lock (_lock)
		{
			_secretPrompts.Add(prompt);
			return Task.FromResult(_secretAnswers.TryDequeue(out SecretPromptResult? answer) ? answer : null);
		}
	}

	public Task<IReadOnlyList<string>?> PromptKeyboardInteractiveAsync(KeyboardInteractivePrompt prompt, CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<string>?>(null);

	public Task<ConfirmResult> ConfirmAsync(ConfirmPrompt prompt, CancellationToken cancellationToken = default) => Task.FromResult(ConfirmResult.No);

	public Task<OverwriteDecision> ConfirmOverwriteAsync(OverwritePrompt prompt, CancellationToken cancellationToken = default) =>
		Task.FromResult(OverwriteDecision.Cancel);

	public void Notify(NoticeSeverity severity, string message, string? title = null)
	{
		lock (_lock)
		{
			_notices.Add(new Notice(severity, message, title));
		}
	}
}
