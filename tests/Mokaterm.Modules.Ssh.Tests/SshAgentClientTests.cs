using System.Buffers.Binary;
using System.Text;
using Mokaterm.Modules.Ssh.Agent;
using Mokaterm.Modules.Ssh.Tests.Fakes;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>The agent protocol against a fake agent on a pipe of its own: framing, parsing and the refusal path.</summary>
public sealed class SshAgentClientTests
{
	private static readonly byte[] Ed25519Blob = Convert.FromBase64String(TestKeys.Ed25519PublicKey);

	[Fact]
	public async Task ListIdentities_ReadsBlobsAndComments()
	{
		byte[] second = [.. Encoding.ASCII.GetBytes("\0\0\0ssh-rsa"), 1, 2, 3];
		await using FakeAgentServer server = new(_ => FakeAgentServer.IdentitiesAnswer((Ed25519Blob, "me@laptop"), (second, "")));
		using SshAgentClient client = server.Connect();

		IReadOnlyList<SshAgentIdentity> identities = client.ListIdentities();

		Assert.Equal(2, identities.Count);
		Assert.Equal("me@laptop", identities[0].Comment);
		Assert.Equal("ssh-ed25519", identities[0].Algorithm);
		Assert.Equal(TestKeys.Ed25519Fingerprint, identities[0].Fingerprint);
		Assert.Equal("me@laptop", identities[0].Display);
		Assert.Equal("ssh-rsa", identities[1].Algorithm);

		// A key with no comment still has something to show in a list.
		Assert.Equal(identities[1].Fingerprint, identities[1].Display);
		Assert.Equal([SshAgentClient.RequestIdentities], Assert.Single(server.Requests));
	}

	[Fact]
	public async Task ListIdentities_OnAnEmptyAgentIsEmpty()
	{
		await using FakeAgentServer server = new(_ => FakeAgentServer.IdentitiesAnswer());
		using SshAgentClient client = server.Connect();

		Assert.Empty(client.ListIdentities());
	}

	[Fact]
	public async Task Sign_SendsTheKeyTheDataAndTheFlagsAndReturnsTheSignature()
	{
		byte[] signature = [9, 8, 7, 6];
		await using FakeAgentServer server = new(_ => FakeAgentServer.SignAnswer(signature));
		using SshAgentClient client = server.Connect();
		byte[] data = Encoding.ASCII.GetBytes("session id and request");

		byte[] answer = client.Sign(Ed25519Blob, data, SshAgentClient.FlagRsaSha2512);

		Assert.Equal(signature, answer);
		SshWireReader request = new(Assert.Single(server.Requests));
		Assert.True(request.TryReadByte(out byte type));
		Assert.Equal(SshAgentClient.SignRequest, type);
		Assert.True(request.TryReadString(out byte[] blob));
		Assert.Equal(Ed25519Blob, blob);
		Assert.True(request.TryReadString(out byte[] signed));
		Assert.Equal(data, signed);
		Assert.True(request.TryReadUInt32(out uint flags));
		Assert.Equal(SshAgentClient.FlagRsaSha2512, flags);
		Assert.Equal(0, request.Remaining);
	}

	[Fact]
	public async Task Sign_TurnsARefusalIntoSomethingReadable()
	{
		await using FakeAgentServer server = new(_ => FakeAgentServer.Failure());
		using SshAgentClient client = server.Connect();

		SshAgentException failure = Assert.Throws<SshAgentException>(() => client.Sign(Ed25519Blob, [1], 0));

		Assert.Contains("refused", failure.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task Request_RefusesAnAnswerOfTheWrongType()
	{
		await using FakeAgentServer server = new(_ => FakeAgentServer.SignAnswer([1, 2]));
		using SshAgentClient client = server.Connect();

		Assert.Throws<SshAgentException>(client.ListIdentities);
	}

	[Fact]
	public async Task Request_RefusesAFrameTooLargeToRead()
	{
		await using FakeAgentServer server = new(_ => new byte[2 * 1024 * 1024]);
		using SshAgentClient client = server.Connect();

		Assert.Throws<SshAgentException>(client.ListIdentities);
	}

	[Fact]
	public async Task Request_ReportsAnAgentThatHangsUp()
	{
		await using FakeAgentServer server = new(_ => throw new IOException("gone"));
		using SshAgentClient client = server.Connect();

		SshAgentException failure = Assert.Throws<SshAgentException>(client.ListIdentities);

		Assert.Contains("closed the connection", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Request_GivesUpOnAnAgentThatNeverAnswers()
	{
		using ManualResetEventSlim release = new();
		await using FakeAgentServer server = new(request =>
		{
			// A hung agent, or a pipe squatted by something that is not one: it takes the request and says nothing.
			_ = release.Wait(TimeSpan.FromSeconds(30));
			return FakeAgentServer.IdentitiesAnswer();
		});

		// The same value is the connect timeout, so it leaves room for a loaded machine; the deadline is what is tested.
		using SshAgentClient client = SshAgentClient.Connect(server.Address, TimeSpan.FromSeconds(1.5));
		Task<SshAgentException> listing = Task.Run(() => Assert.Throws<SshAgentException>(client.ListIdentities), TestContext.Current.CancellationToken);
		try
		{
			SshAgentException failure = await listing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

			Assert.Contains("did not answer", failure.Message, StringComparison.Ordinal);
		}
		finally
		{
			release.Set();
		}
	}

	[Fact]
	public void Connect_ReportsWhenNothingIsListening()
	{
		SshAgentAddress missing = new(SshAgentTransport.NamedPipe, "mokaterm-test-agent-" + Guid.NewGuid().ToString("N"));

		SshAgentException failure = Assert.Throws<SshAgentException>(() => SshAgentClient.Connect(missing, TimeSpan.FromMilliseconds(200)));

		Assert.Contains(missing.Name, failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Connect_WithNoCandidatesSaysWhereToStartOne()
	{
		SshAgentException failure = Assert.Throws<SshAgentException>(() => SshAgentClient.Connect([], TimeSpan.FromMilliseconds(200)));

		Assert.Contains("No SSH agent was found", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Frames_AreBigEndianLengthPrefixed()
	{
		byte[] body = new SshWireWriter().WriteByte(SshAgentClient.SignRequest).WriteString([0xAA, 0xBB]).ToArray();

		Assert.Equal([SshAgentClient.SignRequest, 0, 0, 0, 2, 0xAA, 0xBB], body);
		Span<byte> header = stackalloc byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(header, (uint)body.Length);
		Assert.Equal([0, 0, 0, 7], header.ToArray());
	}
}
