using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;

namespace Mokaterm.Modules.Ssh.Forwarding;

/// <summary>What one session's forwarding is built from.</summary>
internal sealed class SshForwardingSetup
{
	public required Guid SessionId { get; init; }

	public required SshForwardingOptions Options { get; init; }

	/// <summary><c>user@host</c>, for what the user reads.</summary>
	public required string Account { get; init; }

	/// <summary>
	/// Whether the agent running on this machine may be forwarded. False on the web host, where this machine is the
	/// server and its agent belongs to whoever runs it, not to whoever opened the page.
	/// </summary>
	public bool OffersSystemAgent { get; init; } = true;

	/// <summary>Whether X11 may be forwarded. False on the web host, where the X server would have to be the server's.</summary>
	public bool OffersX11 { get; init; } = true;

	/// <summary>The agent the SSH settings name, if any; otherwise the usual places are searched.</summary>
	public string? AgentEndpoint { get; init; }

	/// <summary>
	/// How long the agent gets per request. The most the agent client allows, because a forwarded signature may wait for
	/// a person to confirm it or touch a security key.
	/// </summary>
	public TimeSpan AgentTimeout { get; init; } = TimeSpan.FromSeconds(30);

	/// <summary>The login's credentials, not owned: a saved key there is what the in-process agent serves.</summary>
	public LoginCredentials? Credentials { get; init; }

	public IUserInteraction? Interaction { get; init; }

	/// <summary>Looks up the X server's real cookie for a display; null when it has none.</summary>
	public Func<X11Display, byte[]?> FindRealCookie { get; init; } = XAuthority.FindCookie;

	/// <summary>True when the login signed in with a saved key, which the in-process agent can then serve.</summary>
	public bool HasSavedKey => Credentials is { Method: Abstractions.Connections.AuthenticationMethod.PublicKey, PrivateKey: not null };
}
