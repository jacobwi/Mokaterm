using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Security;
using Mokaterm.Modules.Ssh.Agent;
using Mokaterm.Modules.Ssh.Forwarding;
using Mokaterm.Modules.Ssh.Tests.Fakes;
using Renci.SshNet;
using Renci.SshNet.Messages.Connection;
using LoginMethod = Mokaterm.Abstractions.Connections.AuthenticationMethod;

namespace Mokaterm.Modules.Ssh.Tests;

/// <summary>
/// A session's forwarding end to end, short of a server: a real SSH.NET session raises the messages a server would
/// send, a recording sender keeps what forwarding answers, and the X server and agent at the far end are real sockets
/// and pipes of the test's own.
/// </summary>
public sealed class SshForwardingTests
{
	private const uint ShellChannel = 0;

	private const uint ServerChannel = 9;

	[Fact]
	public async Task ShellOpen_AsksForBothBeforeTheShell()
	{
		_ = SshNetInternals.AcceptUnknownChannelTypes();
		RecordingMessageSender sender = new();
		Session session = SshNetSession.Create();
		await using SshForwarding forwarding = new(session, sender, Setup(agent: true, x11: true, credentials: SavedKey()), NullLogger.Instance);

		OpenShell(session, forwarding);

		ChannelRequestMessage[] requests = [.. sender.OfType<ChannelRequestMessage>()];
		Assert.Equal(["auth-agent-req@openssh.com", "x11-req"], requests.Select(request => request.RequestName));
		Assert.All(requests, request => Assert.Equal(ShellChannel, request.LocalChannelNumber));
		Assert.All(requests, request => Assert.Equal(0, request.RequestData[0]));
		Assert.Equal(SshForwardingState.Requested, forwarding.Agent.State);
		Assert.Equal(SshForwardingState.Requested, forwarding.X11.State);
	}

	[Fact]
	public async Task ShellOpen_OnlyTheFirstConfirmationCarriesTheRequests()
	{
		RecordingMessageSender sender = new();
		Session session = SshNetSession.Create();
		await using SshForwarding forwarding = new(session, sender, Setup(x11: true), NullLogger.Instance);

		forwarding.BeginShellOpen();
		session.Receive(new ChannelOpenConfirmationMessage(1, 1000, 1000, ShellChannel));
		session.Receive(new ChannelOpenConfirmationMessage(2, 1000, 1000, 77));
		forwarding.EndShellOpen(opened: true);
		session.Receive(new ChannelOpenConfirmationMessage(3, 1000, 1000, 78));

		Assert.Equal(ShellChannel, Assert.Single(sender.OfType<ChannelRequestMessage>()).LocalChannelNumber);
	}

	[Fact]
	public async Task ShellOpen_WithoutSeeingTheShellsChannel_SaysSo()
	{
		RecordingInteraction interaction = new();
		RecordingMessageSender sender = new();
		Session session = SshNetSession.Create();
		await using SshForwarding forwarding = new(session, sender, Setup(x11: true, interaction: interaction), NullLogger.Instance);

		forwarding.BeginShellOpen();
		forwarding.EndShellOpen(opened: true);

		Assert.Empty(sender.Sent);
		Assert.Equal(SshForwardingState.Unavailable, forwarding.X11.State);
		Notice notice = Assert.Single(interaction.Notices);
		Assert.Contains("X11 forwarding", notice.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task WebHost_ForwardsNoX11_AndNoAgentWithoutASavedKey()
	{
		RecordingInteraction interaction = new();
		RecordingMessageSender sender = new();
		Session session = SshNetSession.Create();
		SshForwardingSetup setup = Setup(agent: true, x11: true, interaction: interaction, offersLocal: false);
		await using SshForwarding forwarding = new(session, sender, setup, NullLogger.Instance);

		OpenShell(session, forwarding);

		Assert.Empty(sender.Sent);
		Assert.False(forwarding.IsWanted);
		Assert.Equal(SshForwardingState.Unavailable, forwarding.Agent.State);
		Assert.Equal(SshForwardingState.Unavailable, forwarding.X11.State);
		Assert.Contains("web host", forwarding.X11.Problem, StringComparison.Ordinal);
		Assert.Single(interaction.Notices);
	}

	[Fact]
	public void NothingAskedFor_IsNoForwarding()
	{
		using SshClient client = new("127.0.0.1", "tester", "secret");

		Assert.Null(SshForwarding.Start(client, Setup(), NullLogger.Instance));
	}

	[Fact]
	public async Task BadDisplay_IsReportedAndNotAskedFor()
	{
		RecordingMessageSender sender = new();
		Session session = SshNetSession.Create();
		await using SshForwarding forwarding = new(session, sender, Setup(x11: true, display: "unix:0"), NullLogger.Instance);

		OpenShell(session, forwarding);

		Assert.Empty(sender.Sent);
		Assert.Equal(SshForwardingState.Unavailable, forwarding.X11.State);
		Assert.Contains("unix:0", forwarding.X11.Problem, StringComparison.Ordinal);
	}

	[Fact(Timeout = TestTimeouts.NetworkMilliseconds)]
	public async Task X11_StripsTheFakeCookie_AndRelaysBothWays()
	{
		using TcpListener xServer = StartXServer(out string display);
		RecordingMessageSender sender = new();
		Session session = SshNetSession.Create();
		await using SshForwarding forwarding = new(session, sender, Setup(x11: true, display: display), NullLogger.Instance);
		OpenShell(session, forwarding);
		byte[] cookie = FakeCookie(sender);

		session.Receive(SshNetSession.X11Open(ServerChannel));
		using TcpClient accepted = await xServer.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
		uint channel = await ConfirmedChannelAsync(sender);
		Assert.Equal(1, forwarding.X11.OpenChannels);

		byte[] firstRequest = [55, 0, 4, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
		session.Receive(new ChannelDataMessage(channel, [.. X11SetupRequestTests.Setup(bigEndian: false, "MIT-MAGIC-COOKIE-1", cookie), .. firstRequest]));

		NetworkStream xStream = accepted.GetStream();
		byte[] expected = [.. X11SetupRequestTests.Setup(bigEndian: false, "", []), .. firstRequest];
		byte[] received = new byte[expected.Length];
		await xStream.ReadExactlyAsync(received, TestContext.Current.CancellationToken);
		Assert.Equal(expected, received);

		byte[] reply = [1, 0, 11, 0, 0, 0, 0, 0];
		await xStream.WriteAsync(reply, TestContext.Current.CancellationToken);
		await sender.WaitForDataAsync(ServerChannel, reply.Length, "the X server's reply goes back over the channel");
		Assert.Equal(reply, sender.DataTo(ServerChannel));

		session.Receive(new ChannelCloseMessage(channel));
		await sender.WaitForAsync<ChannelCloseMessage>(message => message.LocalChannelNumber == ServerChannel, "the close is answered");
		Assert.Equal(0, await xStream.ReadAsync(new byte[1], TestContext.Current.CancellationToken));
		await Eventually.TrueAsync(() => forwarding.X11.OpenChannels == 0, "the channel is no longer counted");
	}

	[Fact(Timeout = TestTimeouts.NetworkMilliseconds)]
	public async Task X11_PutsTheRealCookieInWhenThereIsOne_BigEndian()
	{
		using TcpListener xServer = StartXServer(out string display);
		byte[] real = [.. Enumerable.Range(200, 16).Select(value => (byte)value)];
		RecordingMessageSender sender = new();
		Session session = SshNetSession.Create();
		SshForwardingSetup setup = Setup(x11: true, display: display, realCookie: real);
		await using SshForwarding forwarding = new(session, sender, setup, NullLogger.Instance);
		OpenShell(session, forwarding);
		byte[] cookie = FakeCookie(sender);
		Assert.NotEqual(real, cookie);

		session.Receive(SshNetSession.X11Open(ServerChannel));
		using TcpClient accepted = await xServer.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
		uint channel = await ConfirmedChannelAsync(sender);
		byte[] setupBytes = X11SetupRequestTests.Setup(bigEndian: true, "MIT-MAGIC-COOKIE-1", cookie);

		// The setup arrives in pieces, as it may over a channel.
		session.Receive(new ChannelDataMessage(channel, setupBytes[..5]));
		session.Receive(new ChannelDataMessage(channel, setupBytes[5..]));

		byte[] expected = X11SetupRequestTests.Setup(bigEndian: true, "MIT-MAGIC-COOKIE-1", real);
		byte[] received = new byte[expected.Length];
		await accepted.GetStream().ReadExactlyAsync(received, TestContext.Current.CancellationToken);
		Assert.Equal(expected, received);
	}

	[Fact(Timeout = TestTimeouts.NetworkMilliseconds)]
	public async Task X11_WithAnotherCookie_GoesNoFurther()
	{
		using TcpListener xServer = StartXServer(out string display);
		RecordingMessageSender sender = new();
		Session session = SshNetSession.Create();
		await using SshForwarding forwarding = new(session, sender, Setup(x11: true, display: display), NullLogger.Instance);
		OpenShell(session, forwarding);
		byte[] wrong = [.. FakeCookie(sender)];
		wrong[0] ^= 0xFF;

		session.Receive(SshNetSession.X11Open(ServerChannel));
		using TcpClient accepted = await xServer.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
		uint channel = await ConfirmedChannelAsync(sender);
		session.Receive(new ChannelDataMessage(channel, X11SetupRequestTests.Setup(bigEndian: false, "MIT-MAGIC-COOKIE-1", wrong)));

		await sender.WaitForAsync<ChannelCloseMessage>(message => message.LocalChannelNumber == ServerChannel, "the channel is closed");
		Assert.Equal(0, await accepted.GetStream().ReadAsync(new byte[64], TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task X11_WithNoXServer_FailsTheChannelAndSaysSoOnce()
	{
		string display = $"127.0.0.1:{LoopbackPort.Free() - X11Display.FirstPort}";
		RecordingInteraction interaction = new();
		RecordingMessageSender sender = new();
		Session session = SshNetSession.Create();
		await using SshForwarding forwarding = new(session, sender, Setup(x11: true, display: display, interaction: interaction), NullLogger.Instance);
		OpenShell(session, forwarding);

		session.Receive(SshNetSession.X11Open(ServerChannel));
		session.Receive(SshNetSession.X11Open(ServerChannel + 1));

		ChannelOpenFailureMessage failure = await sender.WaitForAsync<ChannelOpenFailureMessage>(message => message.LocalChannelNumber == ServerChannel, "the first channel fails");
		_ = await sender.WaitForAsync<ChannelOpenFailureMessage>(message => message.LocalChannelNumber == ServerChannel + 1, "the second channel fails");
		Assert.Equal(2u, failure.ReasonCode);
		Assert.Empty(sender.OfType<ChannelOpenConfirmationMessage>());
		Notice notice = Assert.Single(interaction.Notices);
		Assert.Equal("X11 forwarding", notice.Title);
		Assert.Contains(display, notice.Message, StringComparison.Ordinal);
		Assert.Contains("VcXsrv", notice.Message, StringComparison.Ordinal);
		Assert.Contains("No X server answered", forwarding.X11.Problem, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ChannelsThatWereNotAskedFor_AreRefused()
	{
		RecordingMessageSender sender = new();
		Session session = SshNetSession.Create();
		await using SshForwarding forwarding = new(session, sender, Setup(x11: true, display: "127.0.0.1:0"), NullLogger.Instance);
		OpenShell(session, forwarding);

		session.Receive(SshNetSession.AgentOpen(ServerChannel));

		ChannelOpenFailureMessage failure = await sender.WaitForAsync<ChannelOpenFailureMessage>(_ => true, "the agent channel is refused");
		Assert.Equal((ServerChannel, 1u), (failure.LocalChannelNumber, failure.ReasonCode));
	}

	[Fact]
	public async Task OtherChannelTypes_AreLeftToSshNet()
	{
		RecordingMessageSender sender = new();
		Session session = SshNetSession.Create();
		await using SshForwarding forwarding = new(session, sender, Setup(x11: true, display: "127.0.0.1:0"), NullLogger.Instance);
		OpenShell(session, forwarding);
		int before = sender.Sent.Count;

		ChannelOpenMessage tunnel = new();
		tunnel.Load(SshNetSession.OpenBytes(
			"forwarded-tcpip",
			ServerChannel,
			1000,
			1000,
			new SshWireWriter().WriteString("127.0.0.1"u8).WriteUInt32(8080).WriteString("10.0.0.1"u8).WriteUInt32(5000).ToArray()));
		session.Receive(tunnel);

		await Eventually.StaysTrueAsync(() => sender.Sent.Count == before, "nothing answers a channel forwarding does not own");
	}

	[Fact]
	public async Task Agent_ServesTheSavedKey_WhenTheSystemAgentIsNotOffered()
	{
		Assert.Null(SshNetInternals.AcceptUnknownChannelTypes());
		RecordingMessageSender sender = new();
		Session session = SshNetSession.Create();
		using LoginCredentials credentials = SavedKey();
		await using SshForwarding forwarding = new(session, sender, Setup(agent: true, credentials: credentials, offersLocal: false), NullLogger.Instance);
		OpenShell(session, forwarding);

		session.Receive(SshNetSession.AgentOpen(ServerChannel));
		uint channel = await ConfirmedChannelAsync(sender);

		session.Receive(new ChannelDataMessage(channel, AgentFrameReader.Frame([SshAgentClient.RequestIdentities])));
		byte[] identities = await AnswerAsync(sender, 0);
		SshWireReader list = new(identities);
		Assert.True(list.TryReadByte(out byte type));
		Assert.Equal(SshAgentClient.IdentitiesAnswer, type);
		Assert.True(list.TryReadUInt32(out uint count));
		Assert.Equal(1u, count);
		Assert.True(list.TryReadString(out byte[] blob));
		Assert.Equal(Convert.FromBase64String(TestKeys.Ed25519PublicKey), blob);

		byte[] data = Encoding.ASCII.GetBytes("sign me");
		byte[] request = AgentFrameReader.Frame(KeychainAgentTests.SignRequest(blob, data, 0));
		int sent = sender.DataTo(ServerChannel).Length;
		session.Receive(new ChannelDataMessage(channel, request[..7]));
		session.Receive(new ChannelDataMessage(channel, request[7..]));
		byte[] signature = KeychainAgentTests.ReadSignature(await AnswerAsync(sender, sent));
		using PrivateKeyFile verifier = KeychainAgentTests.Load(TestKeys.Ed25519);
		Assert.True(verifier.HostKeyAlgorithms.Single().VerifySignature(data, signature));
	}

	[Fact]
	public async Task Agent_RelaysToTheSystemAgent_WhenOneAnswers()
	{
		Assert.Null(SshNetInternals.AcceptUnknownChannelTypes());
		byte[] answer = FakeAgentServer.IdentitiesAnswer((Convert.FromBase64String(TestKeys.Ed25519PublicKey), "from the system agent"));
		await using FakeAgentServer agent = new(_ => answer);
		RecordingMessageSender sender = new();
		Session session = SshNetSession.Create();
		using LoginCredentials credentials = SavedKey();
		SshForwardingSetup setup = Setup(agent: true, credentials: credentials, agentEndpoint: agent.Address.Display);
		await using SshForwarding forwarding = new(session, sender, setup, NullLogger.Instance);
		OpenShell(session, forwarding);

		session.Receive(SshNetSession.AgentOpen(ServerChannel));
		uint channel = await ConfirmedChannelAsync(sender);
		byte[] removeAll = [19];
		session.Receive(new ChannelDataMessage(channel, AgentFrameReader.Frame(removeAll)));

		Assert.Equal(answer, await AnswerAsync(sender, 0));
		Assert.Equal(removeAll, Assert.Single(agent.Requests));
	}

	[Fact]
	public async Task Agent_ClosesAChannelThatBreaksTheFraming()
	{
		Assert.Null(SshNetInternals.AcceptUnknownChannelTypes());
		RecordingMessageSender sender = new();
		Session session = SshNetSession.Create();
		using LoginCredentials credentials = SavedKey();
		await using SshForwarding forwarding = new(session, sender, Setup(agent: true, credentials: credentials, offersLocal: false), NullLogger.Instance);
		OpenShell(session, forwarding);

		session.Receive(SshNetSession.AgentOpen(ServerChannel));
		uint channel = await ConfirmedChannelAsync(sender);
		session.Receive(new ChannelDataMessage(channel, [0, 4, 0, 1, 11]));

		await sender.WaitForAsync<ChannelCloseMessage>(message => message.LocalChannelNumber == ServerChannel, "the channel is closed");
		Assert.Empty(sender.OfType<ChannelDataMessage>());
	}

	[Fact]
	public async Task Dispose_ClosesOpenChannels()
	{
		Assert.Null(SshNetInternals.AcceptUnknownChannelTypes());
		RecordingMessageSender sender = new();
		Session session = SshNetSession.Create();
		using LoginCredentials credentials = SavedKey();
		SshForwarding forwarding = new(session, sender, Setup(agent: true, credentials: credentials, offersLocal: false), NullLogger.Instance);
		OpenShell(session, forwarding);
		session.Receive(SshNetSession.AgentOpen(ServerChannel));
		uint channel = await ConfirmedChannelAsync(sender);

		await forwarding.DisposeAsync();

		Assert.Single(sender.OfType<ChannelCloseMessage>(), message => message.LocalChannelNumber == ServerChannel);
		int count = sender.Sent.Count;
		session.Receive(new ChannelDataMessage(channel, AgentFrameReader.Frame([SshAgentClient.RequestIdentities])));
		session.Receive(SshNetSession.AgentOpen(ServerChannel + 1));
		await Eventually.StaysTrueAsync(() => sender.Sent.Count == count, "a closed forwarding answers nothing");
	}

	private static void OpenShell(Session session, SshForwarding forwarding)
	{
		forwarding.BeginShellOpen();
		session.Receive(new ChannelOpenConfirmationMessage(0, 2 * 1024 * 1024, 32 * 1024, ShellChannel));
		forwarding.EndShellOpen(opened: true);
	}

	private static byte[] FakeCookie(RecordingMessageSender sender)
	{
		ChannelRequestMessage request = Assert.Single(sender.OfType<ChannelRequestMessage>(), message => message.RequestName == "x11-req");
		SshWireReader data = new(request.RequestData);
		Assert.True(data.TryReadByte(out _));
		Assert.True(data.TryReadByte(out _));
		Assert.True(data.TryReadText(out _));
		Assert.True(data.TryReadText(out string hex));
		return Convert.FromHexString(hex);
	}

	private static async Task<uint> ConfirmedChannelAsync(RecordingMessageSender sender)
	{
		ChannelOpenConfirmationMessage confirmation = await sender.WaitForAsync<ChannelOpenConfirmationMessage>(
			message => message.LocalChannelNumber == ServerChannel,
			"the channel is accepted");
		Assert.True(confirmation.RemoteChannelNumber >= SshForwarding.FirstChannelNumber);
		Assert.Equal(SshForwarding.WindowBytes, confirmation.InitialWindowSize);
		return confirmation.RemoteChannelNumber;
	}

	/// <summary>The next whole agent message sent back, after the first <paramref name="skip"/> bytes already read.</summary>
	private static async Task<byte[]> AnswerAsync(RecordingMessageSender sender, int skip)
	{
		byte[]? message = null;
		await Eventually.TrueAsync(
			() =>
			{
				AgentFrameReader frames = new();
				byte[] data = sender.DataTo(ServerChannel);
				return data.Length > skip && frames.Append(data.AsSpan(skip)) && frames.TryTake(out message);
			},
			"the agent answers");
		return message!;
	}

	private static TcpListener StartXServer(out string display)
	{
		TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;
		Assert.True(port >= X11Display.FirstPort, "The test X server needs a port above 6000.");
		display = $"127.0.0.1:{port - X11Display.FirstPort}";
		return listener;
	}

	private static LoginCredentials SavedKey() => new()
	{
		Username = "tester",
		Method = LoginMethod.PublicKey,
		PrivateKey = SecretBuffer.FromString(TestKeys.Ed25519),
	};

	private static SshForwardingSetup Setup(
		bool agent = false,
		bool x11 = false,
		string? display = null,
		LoginCredentials? credentials = null,
		IUserInteraction? interaction = null,
		bool offersLocal = true,
		string? agentEndpoint = null,
		byte[]? realCookie = null) => new()
	{
		SessionId = Guid.NewGuid(),
		Options = new SshForwardingOptions { ForwardAgent = agent, ForwardX11 = x11, Display = display },
		Account = "tester@server",
		OffersSystemAgent = offersLocal,
		OffersX11 = offersLocal,
		AgentEndpoint = agentEndpoint,
		AgentTimeout = TimeSpan.FromSeconds(10),
		Credentials = credentials,
		Interaction = interaction,
		FindRealCookie = _ => realCookie is null ? null : [.. realCookie],
	};
}
