namespace Mokaterm.Modules.Ssh.KeepAlive;

/// <summary>What became of one attempt to send a keepalive.</summary>
internal enum KeepAliveSend
{
	/// <summary>The keepalive is counted as owed and on its way out.</summary>
	Sent,

	/// <summary>
	/// Nothing went out this time: a forward request may be waiting for its own reply, the last keepalive is still being
	/// written, or the line stopped asking a server that does not answer. Whatever the server already owes keeps counting.
	/// </summary>
	Waiting,

	/// <summary>The connection is gone, which the session hears about from SSH.NET itself.</summary>
	Closed,
}
