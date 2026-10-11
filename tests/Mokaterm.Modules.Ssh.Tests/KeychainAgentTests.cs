using System.Text;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Modules.Ssh.Agent;
using Mokaterm.Modules.Ssh.Forwarding;
using Mokaterm.Modules.Ssh.Keys;
using Renci.SshNet;
using Renci.SshNet.Security;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>
/// The in-process agent that serves a login's saved key. Signatures are checked with SSH.NET's own verifier for the
/// public half, so a signature that is the wrong shape for the wire fails here rather than on a server.
/// </summary>
public sealed class KeychainAgentTests
{
	private static readonly byte[] Ed25519Blob = Convert.FromBase64String(TestKeys.Ed25519PublicKey);

	[Fact]
	public void RequestIdentities_ListsTheOneKey()
	{
		using KeychainAgent agent = new(Load(TestKeys.Ed25519));

		SshWireReader answer = new(agent.Answer([SshAgentClient.RequestIdentities]));

		Assert.True(answer.TryReadByte(out byte type));
		Assert.Equal(SshAgentClient.IdentitiesAnswer, type);
		Assert.True(answer.TryReadUInt32(out uint count));
		Assert.Equal(1u, count);
		Assert.True(answer.TryReadString(out byte[] blob));
		Assert.Equal(Ed25519Blob, blob);
		Assert.True(answer.TryReadText(out string comment));
		Assert.Equal(KeychainAgent.Comment, comment);
		Assert.Equal(0, answer.Remaining);
		Assert.Equal(TestKeys.Ed25519Fingerprint, agent.Fingerprint);
	}

	[Fact]
	public void SignRequest_SignsWithTheKey()
	{
		using PrivateKeyFile verifier = Load(TestKeys.Ed25519);
		using KeychainAgent agent = new(Load(TestKeys.Ed25519));
		byte[] data = Encoding.ASCII.GetBytes("session id and a userauth request");

		byte[] signature = ReadSignature(agent.Answer(SignRequest(Ed25519Blob, data, 0)));

		Assert.Equal("ssh-ed25519", AlgorithmOf(signature));
		Assert.True(verifier.HostKeyAlgorithms.Single().VerifySignature(data, signature));
		Assert.False(verifier.HostKeyAlgorithms.Single().VerifySignature([.. data, 0], signature));
	}

	[Theory]
	[InlineData(0u, "ssh-rsa")]
	[InlineData(SshAgentClient.FlagRsaSha2256, "rsa-sha2-256")]
	[InlineData(SshAgentClient.FlagRsaSha2512, "rsa-sha2-512")]
	[InlineData(SshAgentClient.FlagRsaSha2256 | SshAgentClient.FlagRsaSha2512, "rsa-sha2-512")]
	public void SignRequest_TakesTheRsaFlags(uint flags, string expected)
	{
		using PrivateKeyFile verifier = Load(TestKeys.EncryptedRsaPem, TestKeys.EncryptedRsaPemPassphrase);
		using KeychainAgent agent = new(Load(TestKeys.EncryptedRsaPem, TestKeys.EncryptedRsaPemPassphrase));
		byte[] blob = verifier.HostKeyAlgorithms.First().Data;
		byte[] data = Encoding.ASCII.GetBytes("data to sign");

		byte[] signature = ReadSignature(agent.Answer(SignRequest(blob, data, flags)));

		Assert.Equal(expected, AlgorithmOf(signature));
		HostAlgorithm algorithm = verifier.HostKeyAlgorithms.Single(candidate => candidate.Name == expected);
		Assert.True(algorithm.VerifySignature(data, signature));
	}

	[Fact]
	public void SignRequest_ForAnotherKey_Fails()
	{
		using KeychainAgent agent = new(Load(TestKeys.Ed25519));
		byte[] other = Convert.FromBase64String(TestKeys.Ed25519PublicKey);
		other[^1] ^= 1;

		Assert.Equal([SshAgentClient.Failure], agent.Answer(SignRequest(other, [1, 2, 3], 0)));
	}

	[Theory]
	[InlineData(new byte[] { })]
	[InlineData(new byte[] { 17 })]
	[InlineData(new byte[] { 18 })]
	[InlineData(new byte[] { 19 })]
	[InlineData(new byte[] { 22, 0, 0, 0, 0 })]
	[InlineData(new byte[] { 27, 0, 0, 0, 4, 113, 117, 101, 114 })]
	[InlineData(new byte[] { 11, 0 })]
	[InlineData(new byte[] { 13, 0, 0 })]
	public void EverythingElse_Fails(byte[] request)
	{
		// Adding, removing, removing all, locking, extensions and malformed requests: the server's programs only sign.
		using KeychainAgent agent = new(Load(TestKeys.Ed25519));

		Assert.Equal([SshAgentClient.Failure], agent.Answer(request));
	}

	[Fact]
	public async Task Exchange_AnswersLikeAnswer()
	{
		using KeychainAgent agent = new(Load(TestKeys.Ed25519));

		byte[] answer = await agent.ExchangeAsync([SshAgentClient.RequestIdentities], TestContext.Current.CancellationToken);

		Assert.Equal(SshAgentClient.IdentitiesAnswer, answer[0]);
	}

	internal static PrivateKeyFile Load(string key, string? passphrase = null)
	{
		PrivateKeyStatus status = SshPrivateKeys.TryLoad(Encoding.UTF8.GetBytes(key), passphrase, out PrivateKeyFile? keyFile, out string? error);
		Assert.True(status == PrivateKeyStatus.Valid, error);
		return keyFile!;
	}

	internal static byte[] SignRequest(byte[] blob, byte[] data, uint flags) =>
		new SshWireWriter().WriteByte(SshAgentClient.SignRequest).WriteString(blob).WriteString(data).WriteUInt32(flags).ToArray();

	internal static byte[] ReadSignature(byte[] answer)
	{
		SshWireReader reader = new(answer);
		Assert.True(reader.TryReadByte(out byte type));
		Assert.Equal(SshAgentClient.SignResponse, type);
		Assert.True(reader.TryReadString(out byte[] signature));
		Assert.Equal(0, reader.Remaining);
		return signature;
	}

	private static string AlgorithmOf(byte[] signature)
	{
		SshWireReader reader = new(signature);
		Assert.True(reader.TryReadText(out string algorithm));
		Assert.True(reader.TryReadString(out byte[] bytes));
		Assert.NotEmpty(bytes);
		return algorithm;
	}
}
