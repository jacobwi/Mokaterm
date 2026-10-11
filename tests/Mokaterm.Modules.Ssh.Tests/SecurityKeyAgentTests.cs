using System.Text;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Modules.Ssh.Agent;
using Mokaterm.Modules.Ssh.Connection;
using Mokaterm.Modules.Ssh.SecurityKeys;
using Mokaterm.Modules.Ssh.Tests.Fakes;
using Renci.SshNet.Security;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>
/// Security keys an SSH agent holds: offered under their own type, signed with the watchdog paused and the agent given
/// the time a touch takes, and the agent's answer, flags and counter included, sent on byte for byte.
/// </summary>
public sealed class SecurityKeyAgentTests : IDisposable
{
	private static readonly byte[] LoginData = Encoding.ASCII.GetBytes("session id, then the whole publickey request");

	private readonly SoftwareSecurityKey _device = new();

	public void Dispose() => _device.Dispose();

	[Theory]
	[InlineData("sk-ecdsa-sha2-nistp256@openssh.com")]
	[InlineData("sk-ssh-ed25519@openssh.com")]
	[InlineData("sk-ssh-ed25519-cert-v01@openssh.com")]
	public void AlgorithmsFor_OffersASecurityKeyUnderItsOwnType(string keyType)
	{
		SshAgentIdentity identity = new() { KeyBlob = new SshWireWriter().WriteString(Encoding.ASCII.GetBytes(keyType)).WriteString([1]).ToArray(), Comment = "" };

		(string Name, uint Flags)[] expected = [(keyType, 0u)];

		Assert.Equal(expected, SshAgentKeySource.AlgorithmsFor(identity));
	}

	[Theory]
	[InlineData(SecurityKeyAlgorithm.EcdsaP256)]
	[InlineData(SecurityKeyAlgorithm.Ed25519)]
	public async Task Sign_SendsTheAgentsSecurityKeySignatureOnUnchanged(SecurityKeyAlgorithm algorithm)
	{
		(byte[] blob, byte[] agentSignature) = SignedByDevice(algorithm);
		await using FakeAgentServer server = new(_ => FakeAgentServer.SignAnswer(agentSignature));
		SshAgentKeySource source = new([new SshAgentIdentity { KeyBlob = blob, Comment = "yubikey" }], server.Connect);
		HostAlgorithm hostAlgorithm = Assert.Single(source.HostKeyAlgorithms);

		byte[] signature = hostAlgorithm.Sign(LoginData);

		Assert.Equal(SecurityKeyTypes.KeyType(algorithm), hostAlgorithm.Name);
		Assert.Equal(agentSignature, signature);
		Assert.True(OpenSshSkVerifier.Verify(blob, signature, LoginData));

		SshWireReader request = new(Assert.Single(server.Requests));
		Assert.True(request.TryReadByte(out _));
		Assert.True(request.TryReadString(out _));
		Assert.True(request.TryReadString(out byte[] data));
		Assert.Equal(LoginData, data);
		Assert.True(request.TryReadUInt32(out uint flags));
		Assert.Equal(0u, flags);
	}

	[Fact(Timeout = TestTimeouts.NetworkMilliseconds)]
	public async Task Sign_WaitsForTheTouchPastTheConnectionsDeadline()
	{
		(byte[] blob, byte[] agentSignature) = SignedByDevice(SecurityKeyAlgorithm.EcdsaP256);
		await using FakeAgentServer server = new(_ =>
		{
			Thread.Sleep(TimeSpan.FromSeconds(1.5));
			return FakeAgentServer.SignAnswer(agentSignature);
		});

		SshAgentKeySource source = new(
			[new SshAgentIdentity { KeyBlob = blob, Comment = "" }],
			() => SshAgentClient.Connect(server.Address, TimeSpan.FromMilliseconds(500)));
		using ConnectWatchdog watchdog = new(TimeSpan.FromMinutes(1));
		RecordingInteraction interaction = new();
		source.Touch.Attach(new SecurityKeyTouch(watchdog, interaction, "me@host", TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));

		Assert.Equal(agentSignature, Assert.Single(source.HostKeyAlgorithms).Sign(LoginData));
		Assert.Contains("Touch your security key", Assert.Single(interaction.Notices).Message, StringComparison.Ordinal);
	}

	[Fact(Timeout = TestTimeouts.NetworkMilliseconds)]
	public async Task Sign_KeepsTheConnectionsDeadlineForAnOrdinaryKey()
	{
		byte[] blob = Convert.FromBase64String(TestKeys.Ed25519PublicKey);
		await using FakeAgentServer server = new(_ =>
		{
			Thread.Sleep(TimeSpan.FromSeconds(1.5));
			return FakeAgentServer.Failure();
		});

		SshAgentKeySource source = new(
			[new SshAgentIdentity { KeyBlob = blob, Comment = "" }],
			() => SshAgentClient.Connect(server.Address, TimeSpan.FromMilliseconds(500)));
		using ConnectWatchdog watchdog = new(TimeSpan.FromMinutes(1));
		RecordingInteraction interaction = new();
		source.Touch.Attach(new SecurityKeyTouch(watchdog, interaction, "me@host", TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));

		SshAgentException failure = Assert.Throws<SshAgentException>(() => Assert.Single(source.HostKeyAlgorithms).Sign(LoginData));

		Assert.Contains("did not answer within 0.5 seconds", failure.Message, StringComparison.Ordinal);
		Assert.Empty(interaction.Notices);
	}

	/// <summary>A key on the software device and a signature it made, as an agent holding that key would return it.</summary>
	private (byte[] Blob, byte[] Signature) SignedByDevice(SecurityKeyAlgorithm algorithm)
	{
		(byte[] credentialId, byte[] publicKey) = _device.Enrol(algorithm, "ssh:");
		SshWireWriter writer = new SshWireWriter().WriteString(Encoding.ASCII.GetBytes(SecurityKeyTypes.KeyType(algorithm)));
		if (algorithm == SecurityKeyAlgorithm.EcdsaP256)
		{
			writer.WriteString("nistp256"u8);
		}

		byte[] blob = writer.WriteString(publicKey).WriteString("ssh:"u8).ToArray();
		SshSecurityKey key = new()
		{
			Algorithm = algorithm,
			PublicKeyBlob = blob,
			Application = "ssh:",
			Flags = SecurityKeyFlags.UserPresenceRequired,
			KeyHandle = credentialId,
		};

		return (blob, Assert.Single(new SecurityKeySource(key, _device).HostKeyAlgorithms).Sign(LoginData));
	}
}
