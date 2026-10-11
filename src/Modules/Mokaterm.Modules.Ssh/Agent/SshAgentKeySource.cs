using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.SecurityKeys;
using Renci.SshNet;
using Renci.SshNet.Security;

namespace Mokaterm.Modules.Ssh.Agent;

/// <summary>
/// The agent's keys as a key source SSH.NET can authenticate with. RSA keys are offered as SHA-2 signatures first
/// because most servers have stopped accepting the SHA-1 <c>ssh-rsa</c> algorithm. Security keys are offered under
/// their own type, as <c>ssh</c> offers them, and sign with the connect watchdog paused while the user touches the key.
/// </summary>
internal sealed class SshAgentKeySource : IPrivateKeySource, ITouchKeySource
{
	private const string RsaKeyType = "ssh-rsa";

	private const string RsaCertificateKeyType = "ssh-rsa-cert-v01@openssh.com";

	public SshAgentKeySource(IEnumerable<SshAgentIdentity> identities, Func<SshAgentClient> connect)
	{
		ArgumentNullException.ThrowIfNull(identities);
		List<HostAlgorithm> algorithms = [];
		foreach (SshAgentIdentity identity in identities)
		{
			SecurityKeyTouchSlot? touch = SshAgentHostAlgorithm.WaitsForTouch(identity.Algorithm) ? Touch : null;
			foreach ((string name, uint flags) in AlgorithmsFor(identity))
			{
				algorithms.Add(new SshAgentHostAlgorithm(name, identity.KeyBlob, flags, connect, touch));
			}
		}

		HostKeyAlgorithms = algorithms;
	}

	public IReadOnlyCollection<HostAlgorithm> HostKeyAlgorithms { get; }

	public SecurityKeyTouchSlot Touch { get; } = new();

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
