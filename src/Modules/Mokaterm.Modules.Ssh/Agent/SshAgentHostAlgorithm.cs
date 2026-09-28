using Renci.SshNet.Security;

namespace Mokaterm.Modules.Ssh.Agent;

/// <summary>
/// One public key algorithm backed by an agent. SSH.NET only ever asks a client key for its name, its blob and a
/// signature, and it puts what <see cref="Sign"/> returns straight into the authentication request: that is the same
/// wire-format blob (algorithm name then signature) an agent's sign response carries, so nothing has to be repacked.
/// </summary>
internal sealed class SshAgentHostAlgorithm : HostAlgorithm
{
	private readonly byte[] _keyBlob;
	private readonly uint _flags;
	private readonly Func<SshAgentClient> _connect;

	/// <param name="name">The public key algorithm name sent to the server, such as <c>rsa-sha2-512</c>.</param>
	/// <param name="keyBlob">The key blob the agent listed, which is also what the server is shown.</param>
	/// <param name="flags">Signature flags for the agent, such as <see cref="SshAgentClient.FlagRsaSha2512"/>.</param>
	public SshAgentHostAlgorithm(string name, byte[] keyBlob, uint flags, Func<SshAgentClient> connect)
		: base(name)
	{
		_keyBlob = keyBlob;
		_flags = flags;
		_connect = connect;
	}

	public override byte[] Data => _keyBlob;

	public override byte[] Sign(byte[] data)
	{
		ArgumentNullException.ThrowIfNull(data);

		// One connection per signature: agents are happy with it, and it keeps the client off SSH.NET's threading model.
		using SshAgentClient client = _connect();
		return client.Sign(_keyBlob, data, _flags);
	}

	/// <summary>
	/// Always false. An agent key is only ever a client key here, so nothing verifies with it, and failing closed is
	/// the safe answer if SSH.NET ever asked.
	/// </summary>
	public override bool VerifySignature(byte[] data, byte[] signature) => false;
}
