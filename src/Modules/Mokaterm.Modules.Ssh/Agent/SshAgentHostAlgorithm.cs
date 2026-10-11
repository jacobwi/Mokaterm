using Mokaterm.Abstractions.Credentials;
using Mokaterm.Modules.Ssh.SecurityKeys;
using Renci.SshNet.Security;

namespace Mokaterm.Modules.Ssh.Agent;

/// <summary>
/// One public key algorithm backed by an agent. SSH.NET only ever asks a client key for its name, its blob and a
/// signature, and it puts what <see cref="Sign"/> returns straight into the authentication request: that is the same
/// wire-format blob (algorithm name then signature) an agent's sign response carries, so nothing has to be repacked.
/// That holds for a security key too, whose signature adds its flags and counter after the signature itself.
/// </summary>
internal sealed class SshAgentHostAlgorithm : HostAlgorithm
{
	private readonly byte[] _keyBlob;
	private readonly uint _flags;
	private readonly Func<SshAgentClient> _connect;
	private readonly SecurityKeyTouchSlot? _touch;

	/// <param name="name">The public key algorithm name sent to the server, such as <c>rsa-sha2-512</c>.</param>
	/// <param name="keyBlob">The key blob the agent listed, which is also what the server is shown.</param>
	/// <param name="flags">Signature flags for the agent, such as <see cref="SshAgentClient.FlagRsaSha2512"/>.</param>
	/// <param name="touch">Set for a security key, whose agent answers only after the user touched the key.</param>
	public SshAgentHostAlgorithm(string name, byte[] keyBlob, uint flags, Func<SshAgentClient> connect, SecurityKeyTouchSlot? touch = null)
		: base(name)
	{
		_keyBlob = keyBlob;
		_flags = flags;
		_connect = connect;
		_touch = touch;
	}

	public override byte[] Data => _keyBlob;

	public override byte[] Sign(byte[] data)
	{
		ArgumentNullException.ThrowIfNull(data);

		if (_touch is null)
		{
			// One connection per signature: agents are happy with it, and it keeps the client off SSH.NET's threading model.
			using SshAgentClient client = _connect();
			return client.Sign(_keyBlob, data, _flags);
		}

		SecurityKeyTouch touch = _touch.Current ?? SecurityKeyTouch.Unattended;
		return touch.Run(gate =>
		{
			using SshAgentClient client = _connect();
			return client.Sign(_keyBlob, data, _flags, gate.Timeout);
		});
	}

	/// <summary>True for the key types whose agent waits for a touch before it answers.</summary>
	public static bool WaitsForTouch(string keyType) => SecurityKeyTypes.IsSecurityKey(keyType);

	/// <summary>
	/// Always false. An agent key is only ever a client key here, so nothing verifies with it, and failing closed is
	/// the safe answer if SSH.NET ever asked.
	/// </summary>
	public override bool VerifySignature(byte[] data, byte[] signature) => false;
}
