using System.Text;
using Mokaterm.Abstractions.Credentials;

namespace Mokaterm.Abstractions.Interaction;

/// <summary>
/// Dialogs that services and protocol modules need while they work. Implemented by the shell with Moka.Red
/// dialogs. Safe to call from any thread; calls queue so only one dialog shows at a time.
/// </summary>
public interface IUserInteraction
{
	Task<HostTrustDecision> ConfirmHostIdentityAsync(HostTrustPrompt prompt, CancellationToken cancellationToken = default);

	/// <summary>Asks for a username and password. Null when cancelled.</summary>
	Task<CredentialPromptResult?> PromptCredentialsAsync(CredentialPrompt prompt, CancellationToken cancellationToken = default);

	/// <summary>Asks for a single secret such as a key passphrase or a sudo password. Null when cancelled.</summary>
	Task<SecretPromptResult?> PromptSecretAsync(SecretPrompt prompt, CancellationToken cancellationToken = default);

	/// <summary>Answers server-driven prompts, in order. Null when cancelled.</summary>
	Task<IReadOnlyList<string>?> PromptKeyboardInteractiveAsync(KeyboardInteractivePrompt prompt, CancellationToken cancellationToken = default);

	/// <summary>A yes or no question. The answer also says whether the user asked not to be asked again.</summary>
	Task<ConfirmResult> ConfirmAsync(ConfirmPrompt prompt, CancellationToken cancellationToken = default);

	Task<OverwriteDecision> ConfirmOverwriteAsync(OverwritePrompt prompt, CancellationToken cancellationToken = default);

	/// <summary>A non-blocking toast.</summary>
	void Notify(NoticeSeverity severity, string message, string? title = null);
}

public enum NoticeSeverity
{
	Info,
	Success,
	Warning,
	Error,
}

public enum HostTrustDecision
{
	Reject,
	AcceptOnce,
	AcceptAndSave,
}

public sealed record HostTrustPrompt
{
	public required Security.HostIdentity Identity { get; init; }

	/// <summary><see cref="Security.HostIdentityMatch.Unknown"/> or <see cref="Security.HostIdentityMatch.Changed"/>.</summary>
	public required Security.HostIdentityMatch Match { get; init; }

	/// <summary>The previously trusted identity when <see cref="Match"/> is Changed.</summary>
	public Security.KnownHost? Previous { get; init; }
}

public sealed record CredentialPrompt
{
	/// <summary>For example "Log in to abc@10.10.2.3".</summary>
	public required string Title { get; init; }

	/// <summary>Extra context, such as "Authentication failed. Try again."</summary>
	public string? Message { get; init; }

	public string? Username { get; init; }

	public bool AllowUsernameEdit { get; init; } = true;

	/// <summary>False for protocols that log in with a password alone, such as VNC, so an empty name is accepted.</summary>
	public bool RequireUsername { get; init; } = true;

	/// <summary>Show a "Save password" option that stores the answer in the vault.</summary>
	public bool OfferSave { get; init; } = true;
}

public sealed record CredentialPromptResult(string Username, string Password, bool Save)
{
	private bool PrintMembers(StringBuilder builder)
	{
		builder
			.Append("Username = ").Append(Username)
			.Append(", Password = ").Append(SecretText.Describe(Password))
			.Append(", Save = ").Append(Save);
		return true;
	}
}

public sealed record SecretPrompt
{
	public required string Title { get; init; }

	public string? Message { get; init; }

	public string Label { get; init; } = "Password";

	/// <summary>Show a "Remember for this session" option.</summary>
	public bool OfferRemember { get; init; }
}

public sealed record SecretPromptResult(string Secret, bool Remember)
{
	private bool PrintMembers(StringBuilder builder)
	{
		builder
			.Append("Secret = ").Append(SecretText.Describe(Secret))
			.Append(", Remember = ").Append(Remember);
		return true;
	}
}

public sealed record KeyboardInteractivePrompt
{
	public required string Title { get; init; }

	public string? Instruction { get; init; }

	public required IReadOnlyList<KeyboardInteractiveQuestion> Questions { get; init; }
}

/// <summary>One server question. <see cref="Echo"/> false means the answer is secret.</summary>
public sealed record KeyboardInteractiveQuestion(string Prompt, bool Echo);

public sealed record ConfirmPrompt
{
	public required string Title { get; init; }

	public required string Message { get; init; }

	public string ConfirmText { get; init; } = "OK";

	public string CancelText { get; init; } = "Cancel";

	/// <summary>Style the confirm button as dangerous, for deletes and overwrites.</summary>
	public bool Destructive { get; init; }

	/// <summary>
	/// Text for a check box that turns this prompt off, such as "Don't ask again". Only for a question the user may
	/// reasonably stop answering, and only where a setting can turn it back on; a delete keeps asking every time.
	/// </summary>
	public string? RememberText { get; init; }

	/// <summary>
	/// What the question is about, shown below the message in a monospace box with its line breaks kept: the first
	/// lines of a paste, a command about to run. <see cref="Message"/> stays prose, so it never carries the text itself.
	/// </summary>
	public string? Details { get; init; }
}

public enum OverwriteDecision
{
	Overwrite,
	Skip,
	OverwriteAll,
	SkipAll,
	Cancel,
}

public sealed record OverwritePrompt
{
	public required string Path { get; init; }

	public long? ExistingSize { get; init; }

	public DateTimeOffset? ExistingModified { get; init; }

	public long? IncomingSize { get; init; }

	public DateTimeOffset? IncomingModified { get; init; }

	/// <summary>Offer the "all" choices because more conflicts may follow.</summary>
	public bool IsBatch { get; init; }
}
