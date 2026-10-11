using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Modules.Ssh.SecurityKeys;

/// <summary>
/// One SSH signature from a security key file through the platform authenticator.
/// </summary>
/// <remarks>
/// WebAuthn signs <c>authenticatorData || SHA-256(clientData)</c>, and no version of webauthn.dll accepts a ready
/// client data hash. It does hash whatever bytes it is given, though, so handing it the SSH message itself makes the
/// signed data exactly the plain OpenSSH form, for ECDSA and Ed25519 alike. That is what libfido2's Windows Hello
/// backend does for OpenSSH on Windows. Should a platform ever insist on JSON and refuse the request, an ECDSA key
/// signs again over the client data OpenSSH defines for its WebAuthn signature type; Ed25519 has no such type.
/// </remarks>
internal static class SecurityKeySigner
{
	/// <exception cref="ProtocolConnectException">The authenticator did not sign, with a message for the user.</exception>
	public static byte[] Sign(SshSecurityKey key, ISecurityKeyAuthenticator authenticator, byte[] message, SecurityKeyTouch touch)
	{
		ArgumentNullException.ThrowIfNull(key);
		ArgumentNullException.ThrowIfNull(authenticator);
		ArgumentNullException.ThrowIfNull(message);
		ArgumentNullException.ThrowIfNull(touch);
		try
		{
			return SignRaw(key, authenticator, message, touch);
		}
		catch (SecurityKeyException ex)
		{
			throw new ProtocolConnectException(ToConnectFailure(ex.Failure), Describe(ex), ex);
		}
	}

	public static ConnectFailure ToConnectFailure(SecurityKeyFailure failure) => failure switch
	{
		SecurityKeyFailure.Cancelled => ConnectFailure.Cancelled,
		SecurityKeyFailure.TimedOut => ConnectFailure.Timeout,
		_ => ConnectFailure.AuthenticationFailed,
	};

	public static string Describe(SecurityKeyException exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		return exception.Failure switch
		{
			SecurityKeyFailure.Cancelled => "The security key prompt was closed, so the login was cancelled.",
			SecurityKeyFailure.TimedOut => "The security key was not touched in time.",
			SecurityKeyFailure.NoCredential => "No security key that holds this login's credential was found. Plug in the key this file was made with.",
			_ => exception.Message,
		};
	}

	private static byte[] SignRaw(SshSecurityKey key, ISecurityKeyAuthenticator authenticator, byte[] message, SecurityKeyTouch touch)
	{
		SecurityKeyAssertion assertion;
		try
		{
			assertion = Assert(key, authenticator, message, touch);
		}
		catch (SecurityKeyException ex) when (ex.Failure == SecurityKeyFailure.InvalidRequest && WebAuthnClientData.CanCarry(key))
		{
			// The refusal comes before the prompt, so trying the other form costs the user nothing.
			byte[] clientData = WebAuthnClientData.Build(message, key.Application);
			SecurityKeyAssertion webAuthn = Assert(key, authenticator, clientData, touch);
			return SecurityKeySignature.EncodeWebAuthn(key, webAuthn, key.Application, clientData);
		}

		return SecurityKeySignature.Encode(key, assertion);
	}

	private static SecurityKeyAssertion Assert(SshSecurityKey key, ISecurityKeyAuthenticator authenticator, byte[] clientData, SecurityKeyTouch touch) =>

		// SSH.NET signs synchronously on its own thread and sends the result as soon as this returns.
		authenticator.GetAssertionAsync(
			new SecurityKeyAssertionRequest
			{
				RelyingPartyId = key.Application,
				ClientData = clientData,
				CredentialId = key.KeyHandle,
				RequireUserVerification = key.RequiresUserVerification,
				Timeout = touch.Timeout,
			},
			touch.CancellationToken).GetAwaiter().GetResult();
}
