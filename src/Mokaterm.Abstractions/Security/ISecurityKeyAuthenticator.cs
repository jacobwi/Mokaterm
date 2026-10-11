namespace Mokaterm.Abstractions.Security;

/// <summary>
/// A FIDO2 authenticator for security key logins: WebAuthn on Windows, which is the one route to a USB key that needs no
/// administrator rights. Only a host that runs on the machine the key is plugged into registers one; the web host does
/// not, because its connections start on the server, far from the user's key.
/// </summary>
/// <remarks>
/// Both calls show the operating system's own prompt and wait for the user, so they take as long as the user does,
/// up to the request's timeout. Cancelling the token closes the prompt.
/// </remarks>
public interface ISecurityKeyAuthenticator
{
	/// <summary>Shown where the keychain offers to make a key, for example "Windows Hello and security keys".</summary>
	string Description { get; }

	/// <summary>False when this machine has no authenticator API at all, such as Windows 10 before version 1903.</summary>
	bool IsAvailable { get; }

	/// <summary>Has the credential sign <c>authenticatorData || SHA-256(ClientData)</c>.</summary>
	/// <exception cref="SecurityKeyException">The user cancelled, the key is missing or the platform refused the request.</exception>
	Task<SecurityKeyAssertion> GetAssertionAsync(SecurityKeyAssertionRequest request, CancellationToken cancellationToken = default);

	/// <summary>Creates a credential on an authenticator the user picks.</summary>
	/// <exception cref="SecurityKeyException">The user cancelled, no key can make one or the platform refused the request.</exception>
	Task<SecurityKeyCredential> MakeCredentialAsync(SecurityKeyCredentialRequest request, CancellationToken cancellationToken = default);
}
