using Mokaterm.Abstractions.Security;

namespace Mokaterm.Abstractions.Credentials;

/// <summary>Decrypted secret material. Dispose as soon as it has been handed to the protocol library.</summary>
public sealed class CredentialSecret : IDisposable
{
	public CredentialSecret(CredentialKind kind, SecretBuffer? password, SecretBuffer? privateKey, SecretBuffer? passphrase, SecretBuffer? proxyPassword = null)
	{
		Kind = kind;
		Password = password;
		PrivateKey = privateKey;
		Passphrase = passphrase;
		ProxyPassword = proxyPassword;
	}

	public CredentialKind Kind { get; }

	public SecretBuffer? Password { get; }

	public SecretBuffer? PrivateKey { get; }

	public SecretBuffer? Passphrase { get; }

	/// <summary>The password of the proxy a login dials through, which has nothing to do with the server's own.</summary>
	public SecretBuffer? ProxyPassword { get; }

	public void Dispose()
	{
		Password?.Dispose();
		PrivateKey?.Dispose();
		Passphrase?.Dispose();
		ProxyPassword?.Dispose();
	}
}
