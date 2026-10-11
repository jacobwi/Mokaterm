using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Security;
using Renci.SshNet.Security;

namespace Mokaterm.Modules.Ssh.SecurityKeys;

/// <summary>
/// A security key file as an SSH.NET client key. The algorithm name is the key type, as OpenSSH sends it, and
/// <see cref="Sign"/> returns the finished signature blob, which SSH.NET puts into the request as it is.
/// </summary>
internal sealed class SecurityKeyHostAlgorithm : HostAlgorithm
{
	private readonly SshSecurityKey _key;
	private readonly ISecurityKeyAuthenticator _authenticator;
	private readonly SecurityKeyTouchSlot _touch;

	public SecurityKeyHostAlgorithm(SshSecurityKey key, ISecurityKeyAuthenticator authenticator, SecurityKeyTouchSlot touch)
		: base(key.KeyType)
	{
		_key = key;
		_authenticator = authenticator;
		_touch = touch;
	}

	public override byte[] Data => _key.PublicKeyBlob;

	public override byte[] Sign(byte[] data)
	{
		ArgumentNullException.ThrowIfNull(data);
		SecurityKeyTouch touch = _touch.Current ?? SecurityKeyTouch.Unattended;
		return touch.Run(gate => SecurityKeySigner.Sign(_key, _authenticator, data, gate));
	}

	/// <summary>Always false: this is only ever a client key, and failing closed is the safe answer if SSH.NET asked.</summary>
	public override bool VerifySignature(byte[] data, byte[] signature) => false;
}
