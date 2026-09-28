using System.Net.Sockets;
using System.Text;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Telnet.Protocol;
using Mokaterm.Modules.Telnet.Tests.Fakes;

namespace Mokaterm.Modules.Telnet.Tests.Loopback;

public sealed class TelnetConnectTests
{
	private readonly List<(byte Command, byte Option)> _opening = [];

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task Connect_OpensTheNegotiationWithEveryOptionTheModuleImplements()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();
		await using TelnetTestSession telnet = await TelnetHarness.OpenAsync(server, cancellationToken, answer: RecordOpeningAsync);

		Assert.Equal(
			[
				(TelnetCommand.Do, TelnetOption.Binary),
				(TelnetCommand.Do, TelnetOption.Echo),
				(TelnetCommand.Do, TelnetOption.SuppressGoAhead),
				(TelnetCommand.Will, TelnetOption.Binary),
				(TelnetCommand.Will, TelnetOption.SuppressGoAhead),
				(TelnetCommand.Will, TelnetOption.TerminalType),
				(TelnetCommand.Will, TelnetOption.NegotiateAboutWindowSize),
			],
			_opening);
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task Connect_ReportsWhatItIsDoing()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();
		RecordingProgress<string> status = new();
		TelnetProtocolProvider provider = TelnetHarness.CreateProvider(TelnetHarness.FastSettings());
		ProtocolConnectContext context = TelnetHarness.CreateContext(server.Port, status: status);
		Task<LoopbackTelnetPeer> accepting = server.AcceptAsync(cancellationToken);
		Task<(IProtocolSession Session, ITerminalChannel Terminal)> connecting = TelnetHarness.ConnectAsync(provider, context, cancellationToken);

		await using LoopbackTelnetPeer peer = await accepting;
		await TelnetHarness.RefuseEverythingAsync(peer, cancellationToken);
		(IProtocolSession session, _) = await connecting;
		await session.DisposeAsync();

		Assert.Contains(status.Reports, message => message.Contains("Connecting to 127.0.0.1", StringComparison.Ordinal));
		Assert.Contains("Negotiating options", status.Reports);
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task Connect_ServerAgrees_SendsTheTerminalTypeWhenItIsAskedFor()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();
		await using TelnetTestSession telnet = await TelnetHarness.OpenAsync(
			server,
			cancellationToken,
			answer: TelnetHarness.AgreeToEverythingAsync);

		// The window size goes out as soon as NAWS is agreed, without the server asking for it.
		TelnetMessage windowSize = await telnet.Peer.ReadMessageAsync(TelnetOption.NegotiateAboutWindowSize, cancellationToken);
		Assert.Equal([0, 100, 0, 40], windowSize.Payload);

		await telnet.Peer.SendSubnegotiationAsync(TelnetOption.TerminalType, [TelnetOption.Send], cancellationToken);
		TelnetMessage answer = await telnet.Peer.ReadMessageAsync(TelnetOption.TerminalType, cancellationToken);

		Assert.Equal(TelnetOption.Is, answer.Payload[0]);
		Assert.Equal("xterm-256color", Encoding.ASCII.GetString(answer.Payload, 1, answer.Payload.Length - 1));
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task Connect_ConnectionWithItsOwnTerminalType_SendsThatOne()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();
		await using TelnetTestSession telnet = await TelnetHarness.OpenAsync(
			server,
			cancellationToken,
			options: TelnetConnectionOptions.Default with { TerminalType = "vt220" },
			answer: TelnetHarness.AgreeToEverythingAsync);

		await telnet.Peer.SendSubnegotiationAsync(TelnetOption.TerminalType, [TelnetOption.Send], cancellationToken);
		TelnetMessage answer = await telnet.Peer.ReadMessageAsync(TelnetOption.TerminalType, cancellationToken);

		Assert.Equal("vt220", Encoding.ASCII.GetString(answer.Payload, 1, answer.Payload.Length - 1));
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task Connect_OptionThisModuleDoesNotImplement_IsRefusedInsteadOfIgnored()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();
		await using TelnetTestSession telnet = await TelnetHarness.OpenAsync(server, cancellationToken);

		await telnet.Peer.SendNegotiationAsync(TelnetCommand.Will, TelnetOption.LineMode, cancellationToken);
		TelnetMessage refusedOffer = await telnet.Peer.ReadMessageAsync(TelnetOption.LineMode, cancellationToken);

		await telnet.Peer.SendNegotiationAsync(TelnetCommand.Do, TelnetOption.NewEnvironment, cancellationToken);
		TelnetMessage refusedRequest = await telnet.Peer.ReadMessageAsync(TelnetOption.NewEnvironment, cancellationToken);

		Assert.Equal(TelnetCommand.Dont, refusedOffer.Command);
		Assert.Equal(TelnetCommand.Wont, refusedRequest.Command);
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task Resize_AfterTheServerAgreedToNaws_SendsTheNewSize()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();
		await using TelnetTestSession telnet = await TelnetHarness.OpenAsync(
			server,
			cancellationToken,
			answer: TelnetHarness.AgreeToEverythingAsync);
		await telnet.Peer.ReadMessageAsync(TelnetOption.NegotiateAboutWindowSize, cancellationToken);

		await telnet.Terminal.ResizeAsync(new TerminalSize(80, 24), cancellationToken);
		TelnetMessage resized = await telnet.Peer.ReadMessageAsync(TelnetOption.NegotiateAboutWindowSize, cancellationToken);

		Assert.Equal([0, 80, 0, 24], resized.Payload);
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task Resize_WithoutNaws_SendsNothingAndKeepsWorking()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();
		await using TelnetTestSession telnet = await TelnetHarness.OpenAsync(server, cancellationToken);

		await telnet.Terminal.ResizeAsync(new TerminalSize(80, 24), cancellationToken);
		await telnet.Terminal.WriteAsync("x"u8.ToArray(), cancellationToken);

		Assert.Equal("x", await telnet.Peer.ReadTextAsync(1, cancellationToken));
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task Connect_WithoutAutomaticLogin_NeverAsksForCredentials()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();
		FakeCredentialSource credentials = new();

		await using TelnetTestSession telnet = await TelnetHarness.OpenAsync(server, cancellationToken, credentials: credentials);

		Assert.Equal(0, credentials.GetCount);
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task Connect_QuietServer_StartsTheSessionAnyway()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();

		// Small devices answer no negotiation at all, so the session has to open once the timeout passes.
		await using TelnetTestSession telnet = await TelnetHarness.OpenAsync(
			server,
			cancellationToken,
			answer: static (_, _) => Task.CompletedTask);

		await telnet.Peer.SendTextAsync("ready\r\n", cancellationToken);

		Assert.Contains("ready", await TelnetHarness.ReadTextAsync(telnet.Terminal, "ready", cancellationToken), StringComparison.Ordinal);
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task Connect_KeepAlive_SendsNopWhileTheSessionIsQuiet()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using LoopbackTelnetServer server = new();
		FakeSettingsService settings = TelnetHarness.FastSettings(new TelnetSettings { NegotiationTimeoutSeconds = 1, KeepAliveSeconds = 1 });

		await using TelnetTestSession telnet = await TelnetHarness.OpenAsync(server, cancellationToken, settings: settings);

		TelnetMessage message = await telnet.Peer.ReadMessageAsync(cancellationToken);
		Assert.Equal(TelnetMessage.Bare(TelnetCommand.Nop), message);
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task Connect_ClosedPort_SaysTheHostRefusedTheConnection()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		LoopbackTelnetServer server = new();
		int port = server.Port;
		server.StopListening();
		await server.DisposeAsync();

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => TelnetHarness.CreateProvider().ConnectAsync(TelnetHarness.CreateContext(port), cancellationToken));

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.Contains("refused", failure.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task Connect_UnknownHost_SaysTheNameCannotBeFound()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		ProtocolConnectContext context = TelnetHarness.CreateContext(23, address: "no-such-host.invalid");

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => TelnetHarness.CreateProvider().ConnectAsync(context, cancellationToken));

		Assert.Equal(ConnectFailure.HostUnreachable, failure.Failure);
		Assert.Contains("no-such-host.invalid", failure.Message, StringComparison.Ordinal);
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task Connect_AddressThatNeverAnswers_GivesUpAfterTheConnectTimeout()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		FakeSettingsService settings = TelnetHarness.FastSettings(new TelnetSettings { ConnectTimeoutSeconds = 1, NegotiationTimeoutSeconds = 1 });

		// 192.0.2.0/24 is reserved for documentation, so nothing answers on it.
		ProtocolConnectContext context = TelnetHarness.CreateContext(23, address: "192.0.2.1");

		ProtocolConnectException failure = await Assert.ThrowsAsync<ProtocolConnectException>(
			() => TelnetHarness.CreateProvider(settings).ConnectAsync(context, cancellationToken));

		// Some networks answer for the router instead of dropping the packet, which is unreachable rather than quiet.
		Assert.Contains(failure.Failure, new[] { ConnectFailure.Timeout, ConnectFailure.HostUnreachable });
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task Connect_Cancelled_StopsWithoutTurningIntoAConnectFailure()
	{
		using CancellationTokenSource cancellation = new();
		await using LoopbackTelnetServer server = new();
		Task connecting = TelnetHarness.CreateProvider(TelnetHarness.FastSettings())
			.ConnectAsync(TelnetHarness.CreateContext(server.Port), cancellation.Token);

		await using LoopbackTelnetPeer peer = await server.AcceptAsync(TestContext.Current.CancellationToken);
		await cancellation.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting);
	}

	[Fact(Timeout = TelnetHarness.TestTimeoutMilliseconds)]
	public async Task Describe_SocketFailures_BecomeMessagesWrittenForTheUser()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;

		ProtocolConnectException timedOut = await TelnetConnectErrors.DescribeAsync(
			new SocketException((int)SocketError.TimedOut),
			"device",
			23,
			timedOut: true,
			cancellationToken);
		ProtocolConnectException reset = await TelnetConnectErrors.DescribeAsync(
			new SocketException((int)SocketError.ConnectionReset),
			"device",
			23,
			timedOut: false,
			cancellationToken);
		ProtocolConnectException closed = await TelnetConnectErrors.DescribeAsync(
			new EndOfStreamException(),
			"device",
			23,
			timedOut: false,
			cancellationToken);

		Assert.Equal(ConnectFailure.Timeout, timedOut.Failure);
		Assert.Equal(ConnectFailure.ProtocolError, reset.Failure);
		Assert.Equal(ConnectFailure.ProtocolError, closed.Failure);
		Assert.Contains("device:23", closed.Message, StringComparison.Ordinal);
	}

	private async Task RecordOpeningAsync(LoopbackTelnetPeer peer, CancellationToken cancellationToken)
	{
		for (int i = 0; i < TelnetHarness.OpeningRequestCount; i++)
		{
			TelnetMessage message = await peer.ReadMessageAsync(cancellationToken);
			_opening.Add((message.Command, message.Option));
			byte reply = message.Command == TelnetCommand.Do ? TelnetCommand.Wont : TelnetCommand.Dont;
			await peer.SendNegotiationAsync(reply, message.Option, cancellationToken);
		}
	}
}
