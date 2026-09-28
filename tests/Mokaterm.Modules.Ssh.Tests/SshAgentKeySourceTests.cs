using System.Text;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Modules.Ssh.Agent;
using Mokaterm.Modules.Ssh.Keys;
using Mokaterm.Modules.Ssh.Tests.Fakes;
using Renci.SshNet;
using Renci.SshNet.Security;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>
/// The bridge between the agent and SSH.NET. The first test is the one that decides whether agent logins can work at
/// all: SSH.NET puts whatever a key algorithm's Sign returns straight into the authentication request, and an agent's
/// sign response already carries exactly that shape.
/// </summary>
public sealed class SshAgentKeySourceTests
{
	private static readonly byte[] Ed25519Blob = Convert.FromBase64String(TestKeys.Ed25519PublicKey);

	private static readonly byte[] SignedData = Encoding.ASCII.GetBytes("the session id and the request");

	[Fact]
	public void SshNet_SignsIntoTheSameWireShapeAnAgentAnswersWith()
	{
		using PrivateKeyFile keyFile = LoadEd25519();
		HostAlgorithm algorithm = keyFile.HostKeyAlgorithms.First();

		byte[] blob = algorithm.Sign(SignedData);

		SshWireReader reader = new(blob);
		Assert.True(reader.TryReadText(out string algorithmName));
		Assert.Equal(algorithm.Name, algorithmName);
		Assert.True(reader.TryReadString(out byte[] signature));
		Assert.NotEmpty(signature);
		Assert.Equal(0, reader.Remaining);
		Assert.Equal(Ed25519Blob, algorithm.Data);
	}

	[Fact]
	public async Task Sign_GoesToTheAgentAndComesBackUnchanged()
	{
		using PrivateKeyFile keyFile = LoadEd25519();
		HostAlgorithm real = keyFile.HostKeyAlgorithms.First();
		await using FakeAgentServer server = new(request => Answer(request, real));

		SshAgentKeySource source = new([Identity("me@laptop")], server.Connect);
		HostAlgorithm agentAlgorithm = Assert.Single(source.HostKeyAlgorithms);

		Assert.Equal("ssh-ed25519", agentAlgorithm.Name);
		Assert.Equal(Ed25519Blob, agentAlgorithm.Data);
		Assert.Equal(real.Sign(SignedData), agentAlgorithm.Sign(SignedData));
		Assert.False(agentAlgorithm.VerifySignature(SignedData, [1, 2, 3]));
	}

	[Fact]
	public void AlgorithmsFor_OffersRsaWithSha2First()
	{
		SshAgentIdentity rsa = new() { KeyBlob = Blob("ssh-rsa"), Comment = "rsa" };

		IReadOnlyList<(string Name, uint Flags)> algorithms = SshAgentKeySource.AlgorithmsFor(rsa);

		(string Name, uint Flags)[] expected =
		[
			("rsa-sha2-512", SshAgentClient.FlagRsaSha2512),
			("rsa-sha2-256", SshAgentClient.FlagRsaSha2256),
			("ssh-rsa", 0u),
		];

		Assert.Equal(expected, algorithms);
	}

	[Fact]
	public void AlgorithmsFor_OffersOneAlgorithmForEveryOtherKeyType()
	{
		SshAgentIdentity ecdsa = new() { KeyBlob = Blob("ecdsa-sha2-nistp256"), Comment = "" };

		(string Name, uint Flags)[] expected = [("ecdsa-sha2-nistp256", 0u)];

		Assert.Equal(expected, SshAgentKeySource.AlgorithmsFor(ecdsa));
	}

	[Fact]
	public void AlgorithmsFor_SkipsABlobThatIsNotAKey() =>
		Assert.Empty(SshAgentKeySource.AlgorithmsFor(new SshAgentIdentity { KeyBlob = [0, 0], Comment = "" }));

	[Fact]
	public async Task CreateKeySource_NarrowsToTheKeyTheConnectionAsksFor()
	{
		byte[] other = Blob("ssh-rsa");
		await using FakeAgentServer server = new(_ => FakeAgentServer.Failure());
		SshAgentSnapshot snapshot = new(
			server.Address,
			[Identity("mine"), new SshAgentIdentity { KeyBlob = other, Comment = "theirs" }]);

		SshAgentKeySource narrowed = SshAgents.CreateKeySource(snapshot, TestKeys.Ed25519Fingerprint);

		Assert.Equal("ssh-ed25519", Assert.Single(narrowed.HostKeyAlgorithms).Name);

		// No fingerprint offers every key the agent holds, RSA as three algorithms.
		Assert.Equal(4, SshAgents.CreateKeySource(snapshot, null).HostKeyAlgorithms.Count);
	}

	[Fact]
	public async Task CreateKeySource_ExplainsAnAgentThatCannotHelp()
	{
		await using FakeAgentServer server = new(_ => FakeAgentServer.Failure());
		SshAgentSnapshot empty = new(server.Address, []);
		SshAgentSnapshot other = new(server.Address, [Identity("mine")]);

		Assert.Contains("holds no keys", Assert.Throws<SshAgentException>(() => SshAgents.CreateKeySource(empty, null)).Message, StringComparison.Ordinal);
		Assert.Contains("no longer holds", Assert.Throws<SshAgentException>(() => SshAgents.CreateKeySource(other, "SHA256:gone")).Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Read_PicksTheAgentTheSettingsName()
	{
		await using FakeAgentServer server = new(_ => FakeAgentServer.IdentitiesAnswer((Ed25519Blob, "me@laptop")));

		SshAgentSnapshot snapshot = SshAgents.Read(server.PipeName);

		Assert.Equal(server.Address, snapshot.Address);
		Assert.Equal("me@laptop", Assert.Single(snapshot.Identities).Comment);
	}

	[Fact]
	public async Task TryRead_NeverReturnsAnAgentThatIsNotThere()
	{
		// This machine may well run a real agent, so the test only insists that the missing one is not reported.
		string missing = "mokaterm-test-agent-" + Guid.NewGuid().ToString("N");

		SshAgentSnapshot? snapshot = await SshAgents.TryReadAsync(missing, TestContext.Current.CancellationToken);

		Assert.NotEqual(missing, snapshot?.Address.Name);
	}

	private static PrivateKeyFile LoadEd25519()
	{
		PrivateKeyStatus status = SshPrivateKeys.TryLoad(Encoding.UTF8.GetBytes(TestKeys.Ed25519), null, out PrivateKeyFile? keyFile, out string? error);

		Assert.Equal(PrivateKeyStatus.Valid, status);
		Assert.Null(error);
		return keyFile!;
	}

	private static SshAgentIdentity Identity(string comment) => new() { KeyBlob = Ed25519Blob, Comment = comment };

	/// <summary>An SSH public key blob header: the key type as one SSH string. Enough for the algorithm choice.</summary>
	private static byte[] Blob(string keyType) =>
		new SshWireWriter().WriteString(Encoding.ASCII.GetBytes(keyType)).WriteString([1, 2, 3]).ToArray();

	private static byte[] Answer(byte[] request, HostAlgorithm key)
	{
		SshWireReader reader = new(request);
		Assert.True(reader.TryReadByte(out byte type));
		Assert.Equal(SshAgentClient.SignRequest, type);
		Assert.True(reader.TryReadString(out byte[] blob));
		Assert.Equal(Ed25519Blob, blob);
		Assert.True(reader.TryReadString(out byte[] data));
		return FakeAgentServer.SignAnswer(key.Sign(data));
	}
}
