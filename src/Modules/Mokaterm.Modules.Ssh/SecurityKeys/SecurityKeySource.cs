using System.Security.Cryptography;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Security;
using Renci.SshNet;
using Renci.SshNet.Security;

namespace Mokaterm.Modules.Ssh.SecurityKeys;

/// <summary>A security key file as the key source of a publickey login. It holds one algorithm: the key's own type.</summary>
internal sealed class SecurityKeySource : IPrivateKeySource, ITouchKeySource, IDisposable
{
	private readonly SshSecurityKey _key;

	public SecurityKeySource(SshSecurityKey key, ISecurityKeyAuthenticator authenticator)
	{
		ArgumentNullException.ThrowIfNull(key);
		ArgumentNullException.ThrowIfNull(authenticator);
		_key = key;
		HostKeyAlgorithms = [new SecurityKeyHostAlgorithm(key, authenticator, Touch)];
	}

	public IReadOnlyCollection<HostAlgorithm> HostKeyAlgorithms { get; }

	public SecurityKeyTouchSlot Touch { get; } = new();

	/// <summary>The handle is no secret on its own, but it came out of the encrypted half of the file, so it goes the same way.</summary>
	public void Dispose() => CryptographicOperations.ZeroMemory(_key.KeyHandle);
}
