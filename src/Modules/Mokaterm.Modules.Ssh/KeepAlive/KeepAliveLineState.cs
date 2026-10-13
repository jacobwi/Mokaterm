namespace Mokaterm.Modules.Ssh.KeepAlive;

/// <summary>One look at a connection's global requests.</summary>
/// <param name="Replies">
/// Replies received so far, success or failure, whichever request they answered. Only ever grows; more than none means
/// the server answers global requests, so the connection can be judged.
/// </param>
/// <param name="Owed">Requests sent, ours and SSH.NET's, that the server has not answered yet.</param>
/// <param name="Closed">The connection is gone or the line was closed.</param>
/// <param name="Ignored">
/// The server answered none of the keepalives it was sent, so none is sent and nothing is owed until it answers anything.
/// </param>
internal readonly record struct KeepAliveLineState(long Replies, int Owed, bool Closed, bool Ignored = false);
