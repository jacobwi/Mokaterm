namespace Mokaterm.Modules.Ssh.KeepAlive;

/// <summary>What <see cref="KeepAliveMonitor"/> needs from one connection: what it owes and heard, and a way to ask again.</summary>
internal interface IKeepAliveLine
{
	KeepAliveLineState State { get; }

	/// <summary>
	/// Sends one <c>keepalive@openssh.com</c> that wants a reply, unless the line stopped asking, a forward request may be
	/// outstanding or the last keepalive is still being written. Never waits for the write.
	/// </summary>
	KeepAliveSend TrySend();

	/// <summary>
	/// The server answered none of the keepalives it was sent: stop asking, take what is owed as never coming, and let
	/// forward requests run as SSH.NET runs them. Any reply that still arrives starts the counting over.
	/// </summary>
	void StopAsking();
}
