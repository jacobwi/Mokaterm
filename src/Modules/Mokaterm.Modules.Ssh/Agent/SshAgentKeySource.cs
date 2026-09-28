using Mokaterm.Modules.Ssh.Connection;
using Renci.SshNet;
using Renci.SshNet.Security;

namespace Mokaterm.Modules.Ssh.Agent;

/// <summary>
/// The agent's keys as a key source SSH.NET can authenticate with. RSA keys are offered as SHA-2 signatures first
/// because most servers have stopped accepting the SHA-1 <c>ssh-rsa</c> algorithm.
/// </summary>
internal sealed class SshAgentKeySource : IPrivateKeySource
{
	private const string RsaKeyType = "ssh-rsa";

	private const string RsaCertificateKeyType = "ssh-rsa-cert-v01@openssh.com";

	public SshAgentKeySource(IEnumerable<SshAgentIdentity> identities, Func<SshAgentClient> connect)
	{
		ArgumentNullException.ThrowIfNull(identities);
		List<HostAlgorithm> algorithms = [];
		foreach (SshAgentIdentity identity in identities)
		{
			foreach ((string name, uint flags) in AlgorithmsFor(identity))
			{
				algorithms.Add(new SshAgentHostAlgorithm(name, identity.KeyBlob, flags, connect));
			}
		}

		HostKeyAlgorithms = algorithms;
	}

	public IReadOnlyCollection<HostAlgorithm> HostKeyAlgorithms { get; }

	/// <summary>The public key algorithms one identity can sign with, most preferred first.</summary>
	public static IReadOnlyList<(string Name, uint Flags)> AlgorithmsFor(SshAgentIdentity identity)
	{
		ArgumentNullException.ThrowIfNull(identity);
		string keyType = SshHostKeys.ReadKeyType(identity.KeyBlob) ?? "";
		return keyType switch
		{
			RsaKeyType =>
			[
				("rsa-sha2-512", SshAgentClient.FlagRsaSha2512),
				("rsa-sha2-256", SshAgentClient.FlagRsaSha2256),
				(RsaKeyType, 0),
			],
			RsaCertificateKeyType =>
			[
				("rsa-sha2-512-cert-v01@openssh.com", SshAgentClient.FlagRsaSha2512),
				("rsa-sha2-256-cert-v01@openssh.com", SshAgentClient.FlagRsaSha2256),
				(RsaCertificateKeyType, 0),
			],
			"" => [],
			_ => [(keyType, 0)],
		};
	}
}
