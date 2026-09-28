using Mokaterm.Abstractions.Connections;

namespace Mokaterm.Abstractions.Credentials;

/// <summary>
/// Supplies login material for one connection attempt. The session manager creates one per connect, backed
/// by the saved credential and falling back to a prompt when nothing is saved.
/// </summary>
public interface ICredentialSource
{
	/// <summary>The saved username, or null when the user has to be asked.</summary>
	string? Username { get; }

	AuthenticationMethod Method { get; }

	/// <summary>Credentials for the first attempt. Null means the user cancelled the prompt.</summary>
	ValueTask<LoginCredentials?> GetAsync(CancellationToken cancellationToken);

	/// <summary>
	/// Called after the server rejected the previous credentials. Asks again, showing <paramref name="reason"/>.
	/// Null means the user gave up.
	/// </summary>
	ValueTask<LoginCredentials?> RetryAsync(string reason, CancellationToken cancellationToken);
}
