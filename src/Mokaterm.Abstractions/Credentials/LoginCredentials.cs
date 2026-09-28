using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Security;

namespace Mokaterm.Abstractions.Credentials;

/// <summary>
/// Decrypted login material for a connection attempt. A session may keep a copy for companion connections
/// (the SFTP side panel) and sudo prompts, and must dispose it when the session closes.
/// </summary>
public sealed class LoginCredentials : IDisposable
{
	public required string Username { get; init; }

	public required AuthenticationMethod Method { get; init; }

	public SecretBuffer? Password { get; init; }

	public SecretBuffer? PrivateKey { get; init; }

	public SecretBuffer? Passphrase { get; init; }

	/// <summary>
	/// The password of the proxy the connection dials through, which is not the server's. Unset means the proxy needs no
	/// login, or nothing is saved for it and the module asks.
	/// </summary>
	public SecretBuffer? ProxyPassword { get; init; }

	/// <summary>An independent copy with its own buffers.</summary>
	public LoginCredentials Copy() => new()
	{
		Username = Username,
		Method = Method,
		Password = Password?.Copy(),
		PrivateKey = PrivateKey?.Copy(),
		Passphrase = Passphrase?.Copy(),
		ProxyPassword = ProxyPassword?.Copy(),
	};

	/// <summary>
	/// A copy carrying <paramref name="proxyPassword"/> instead of the one it holds, for a password typed once that a
	/// retry and the session's own copies keep. The caller still owns both this instance and the buffer passed in.
	/// </summary>
	public LoginCredentials WithProxyPassword(SecretBuffer? proxyPassword) => new()
	{
		Username = Username,
		Method = Method,
		Password = Password?.Copy(),
		PrivateKey = PrivateKey?.Copy(),
		Passphrase = Passphrase?.Copy(),
		ProxyPassword = proxyPassword?.Copy(),
	};

	public void Dispose()
	{
		Password?.Dispose();
		PrivateKey?.Dispose();
		Passphrase?.Dispose();
		ProxyPassword?.Dispose();
	}
}
