using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Modules.Telnet.Tests.Loopback;

/// <summary>A connected pair: the session under test and the server side the test drives.</summary>
internal sealed class TelnetTestSession : IAsyncDisposable
{
	public TelnetTestSession(IProtocolSession session, ITerminalChannel terminal, LoopbackTelnetPeer peer)
	{
		Session = session;
		Terminal = terminal;
		Peer = peer;
	}

	public IProtocolSession Session { get; }

	public ITerminalChannel Terminal { get; }

	public LoopbackTelnetPeer Peer { get; }

	public async ValueTask DisposeAsync()
	{
		await Session.DisposeAsync();
		await Peer.DisposeAsync();
	}
}
