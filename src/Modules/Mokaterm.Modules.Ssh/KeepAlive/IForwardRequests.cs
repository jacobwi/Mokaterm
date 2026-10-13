namespace Mokaterm.Modules.Ssh.KeepAlive;

/// <summary>
/// How the tunnel manager starts and stops SSH.NET's remote forwards, which are the only global requests SSH.NET sends on
/// its own: through the session's <see cref="GlobalRequestLine"/>, so a keepalive reply can never be taken for theirs.
/// </summary>
internal interface IForwardRequests
{
	/// <summary>
	/// Runs <paramref name="start"/>, which sends one <c>tcpip-forward</c> and waits for its reply, once the server owes no
	/// other reply. No keepalive goes out until it returned.
	/// </summary>
	/// <exception cref="TimeoutException">An earlier request stayed unanswered, so the forward was not asked for.</exception>
	Task StartAsync(Action start, CancellationToken cancellationToken);

	/// <summary>
	/// Runs <paramref name="stop"/>, counting the <c>cancel-tcpip-forward</c> it sends when <paramref name="sendsRequest"/>,
	/// whose reply SSH.NET may stop waiting for before it arrives. Never cancelled: a stop that is skipped leaves a port
	/// listening on the server.
	/// </summary>
	Task StopAsync(Action stop, bool sendsRequest);
}
