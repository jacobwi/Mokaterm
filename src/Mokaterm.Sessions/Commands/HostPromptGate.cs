using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Sessions.Commands;

/// <summary>
/// The questions one host's connect puts to the user during a command run. When the run skips hosts that would ask,
/// every question gets the answer a closed dialog gives and the first one is remembered as the reason the host was
/// skipped, so a provider fails the way it fails for a user who said no and the run shows it as skipped rather than
/// failed. Otherwise the questions reach the window as usual and the host's clock stops while one is open.
/// </summary>
internal sealed class HostPromptGate : IUserInteraction
{
	private readonly IUserInteraction _inner;
	private readonly HostDeadline _deadline;
	private string? _skippedBecause;

	public HostPromptGate(IUserInteraction inner, bool skipPrompts, HostDeadline deadline)
	{
		_inner = inner;
		SkipsPrompts = skipPrompts;
		_deadline = deadline;
	}

	public bool SkipsPrompts { get; }

	/// <summary>The first question the connect would have asked, as the reason the host was skipped; null when it asked nothing.</summary>
	public string? SkippedBecause => Volatile.Read(ref _skippedBecause);

	/// <summary>The reason for a host whose identity the policy would ask about.</summary>
	public static string DescribeIdentity(HostIdentityKind kind, HostIdentityMatch match)
	{
		string what = kind == HostIdentityKind.TlsCertificate ? "certificate" : "host key";
		return match == HostIdentityMatch.Changed
			? $"It would ask about its {what}, which changed since it was trusted."
			: $"It would ask about its {what}, which is not trusted yet.";
	}

	/// <summary>Records why the host was skipped. Only the first reason is kept: later questions follow from the first refusal.</summary>
	public void Skip(string reason) => Interlocked.CompareExchange(ref _skippedBecause, reason, null);

	public async Task<HostTrustDecision> ConfirmHostIdentityAsync(HostTrustPrompt prompt, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(prompt);
		if (SkipsPrompts)
		{
			Skip(DescribeIdentity(prompt.Identity.Kind, prompt.Match));
			return HostTrustDecision.Reject;
		}

		using (_deadline.Pause())
		{
			return await _inner.ConfirmHostIdentityAsync(prompt, cancellationToken);
		}
	}

	public async Task<CredentialPromptResult?> PromptCredentialsAsync(CredentialPrompt prompt, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(prompt);
		if (SkipsPrompts)
		{
			// The prompt's own message says why it asks (the saved login was refused, the key has no user name), which is
			// worth more than "it would ask" alone.
			Skip(string.IsNullOrWhiteSpace(prompt.Message)
				? "It would ask for the login."
				: $"It would ask for the login: {prompt.Message.Trim().TrimEnd('.')}.");
			return null;
		}

		using (_deadline.Pause())
		{
			return await _inner.PromptCredentialsAsync(prompt, cancellationToken);
		}
	}

	public async Task<SecretPromptResult?> PromptSecretAsync(SecretPrompt prompt, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(prompt);
		if (SkipsPrompts)
		{
			Skip($"It would ask for the {InSentence(prompt.Label)}.");
			return null;
		}

		using (_deadline.Pause())
		{
			return await _inner.PromptSecretAsync(prompt, cancellationToken);
		}
	}

	public async Task<IReadOnlyList<string>?> PromptKeyboardInteractiveAsync(KeyboardInteractivePrompt prompt, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(prompt);
		if (SkipsPrompts)
		{
			Skip("It would ask the server's login questions.");
			return null;
		}

		using (_deadline.Pause())
		{
			return await _inner.PromptKeyboardInteractiveAsync(prompt, cancellationToken);
		}
	}

	public async Task<ConfirmResult> ConfirmAsync(ConfirmPrompt prompt, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(prompt);
		if (SkipsPrompts)
		{
			Skip($"It would ask: {prompt.Title}.");
			return ConfirmResult.No;
		}

		using (_deadline.Pause())
		{
			return await _inner.ConfirmAsync(prompt, cancellationToken);
		}
	}

	public async Task<OverwriteDecision> ConfirmOverwriteAsync(OverwritePrompt prompt, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(prompt);
		if (SkipsPrompts)
		{
			Skip("It would ask whether to replace a file.");
			return OverwriteDecision.Cancel;
		}

		using (_deadline.Pause())
		{
			return await _inner.ConfirmOverwriteAsync(prompt, cancellationToken);
		}
	}

	public void Notify(NoticeSeverity severity, string message, string? title = null) => _inner.Notify(severity, message, title);

	/// <summary>A field label such as "Proxy password" as it reads inside a sentence.</summary>
	private static string InSentence(string label)
	{
		string text = label.Trim();
		return text.Length == 0 ? "password" : char.ToLowerInvariant(text[0]) + text[1..];
	}
}
