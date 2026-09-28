using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Mokaterm.Abstractions.Credentials;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Telnet.Protocol;
using Mokaterm.Modules.Telnet.Tests.Fakes;

namespace Mokaterm.Modules.Telnet.Tests.Loopback;

/// <summary>Builds providers, connect contexts and sessions for the loopback tests.</summary>
internal static class TelnetHarness
{
	public const int TestTimeoutMilliseconds = TestTimeouts.NetworkMilliseconds;

	/// <summary>How many options a new session asks about before it starts.</summary>
	public const int OpeningRequestCount = 7;

	public static TerminalSize Size { get; } = new(100, 40);

	public static TelnetProtocolProvider CreateProvider(FakeSettingsService? settings = null) =>
		new(settings ?? new FakeSettingsService(), TimeProvider.System, NullLoggerFactory.Instance);

	public static ProtocolConnectContext CreateContext(
		int port,
		TelnetConnectionOptions? options = null,
		ICredentialSource? credentials = null,
		RecordingProgress<string>? status = null,
		string address = "127.0.0.1",
		TerminalSize? size = null) =>
		new ConnectContext
		{
			ProtocolId = TelnetProtocolProvider.ProtocolId,
			Port = port,
			Address = address,
			Credentials = credentials ?? new FakeCredentialSource(),
			Verifier = new RefusingHostVerifier(),
			Options = (options ?? TelnetConnectionOptions.Default).ApplyTo(ProtocolOptions.Empty),
			Status = status,
			TerminalSize = size ?? Size,
		}.Build();

	/// <summary>Settings that keep the tests quick: a session gives up on a quiet server after one second.</summary>
	public static FakeSettingsService FastSettings(TelnetSettings? settings = null)
	{
		FakeSettingsService service = new();
		service.Set(settings ?? new TelnetSettings { NegotiationTimeoutSeconds = 1 });
		return service;
	}

	/// <summary>Opens a session and returns it with its terminal channel.</summary>
	public static async Task<(IProtocolSession Session, ITerminalChannel Terminal)> ConnectAsync(
		TelnetProtocolProvider provider,
		ProtocolConnectContext context,
		CancellationToken cancellationToken)
	{
		IProtocolSession session = await provider.ConnectAsync(context, cancellationToken);
		ITerminalChannel terminal = session.GetFeature<ITerminalChannel>()
			?? throw new InvalidOperationException("A telnet session must expose a terminal channel.");
		return (session, terminal);
	}

	/// <summary>
	/// Connects to <paramref name="server"/> and lets <paramref name="answer"/> play the server half of the opening
	/// negotiation while the connect is still running.
	/// </summary>
	public static async Task<TelnetTestSession> OpenAsync(
		LoopbackTelnetServer server,
		CancellationToken cancellationToken,
		TelnetConnectionOptions? options = null,
		ICredentialSource? credentials = null,
		FakeSettingsService? settings = null,
		TerminalSize? size = null,
		Func<LoopbackTelnetPeer, CancellationToken, Task>? answer = null)
	{
		ArgumentNullException.ThrowIfNull(server);
		TelnetProtocolProvider provider = CreateProvider(settings ?? FastSettings());
		ProtocolConnectContext context = CreateContext(server.Port, options, credentials, size: size);
		Task<LoopbackTelnetPeer> accepting = server.AcceptAsync(cancellationToken);
		Task<(IProtocolSession Session, ITerminalChannel Terminal)> connecting = ConnectAsync(provider, context, cancellationToken);

		LoopbackTelnetPeer peer = await accepting;
		await (answer ?? RefuseEverythingAsync)(peer, cancellationToken);
		(IProtocolSession session, ITerminalChannel terminal) = await connecting;
		return new TelnetTestSession(session, terminal, peer);
	}

	/// <summary>Answers the opening requests with WONT and DONT, the way a device that implements nothing does.</summary>
	public static Task RefuseEverythingAsync(LoopbackTelnetPeer peer, CancellationToken cancellationToken) =>
		AnswerOpeningAsync(peer, agree: false, cancellationToken);

	/// <summary>Agrees to every opening request, the way a full telnet server does.</summary>
	public static Task AgreeToEverythingAsync(LoopbackTelnetPeer peer, CancellationToken cancellationToken) =>
		AnswerOpeningAsync(peer, agree: true, cancellationToken);

	private static async Task AnswerOpeningAsync(LoopbackTelnetPeer peer, bool agree, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(peer);
		for (int i = 0; i < OpeningRequestCount; i++)
		{
			TelnetMessage request = await peer.ReadMessageAsync(cancellationToken);
			byte reply = request.Command switch
			{
				TelnetCommand.Do => agree ? TelnetCommand.Will : TelnetCommand.Wont,
				TelnetCommand.Will => agree ? TelnetCommand.Do : TelnetCommand.Dont,
				_ => throw new InvalidOperationException($"A new session should not send {request}."),
			};

			await peer.SendNegotiationAsync(reply, request.Option, cancellationToken);
		}
	}

	/// <summary>Reads from the channel until <paramref name="expected"/> has arrived, or the read ends.</summary>
	public static async Task<string> ReadTextAsync(ITerminalChannel terminal, string expected, CancellationToken cancellationToken) =>
		await ReadTextAsync(terminal, Encoding.UTF8, text => text.Contains(expected, StringComparison.Ordinal), cancellationToken);

	public static async Task<string> ReadTextAsync(
		ITerminalChannel terminal,
		Encoding encoding,
		Func<string, bool> until,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(terminal);
		ArgumentNullException.ThrowIfNull(until);
		List<byte> received = [];
		byte[] buffer = new byte[4096];
		while (true)
		{
			string text = encoding.GetString([.. received]);
			if (until(text))
			{
				return text;
			}

			int read = await terminal.ReadAsync(buffer, cancellationToken);
			if (read == 0)
			{
				return text;
			}

			received.AddRange(buffer.AsSpan(0, read));
		}
	}
}
