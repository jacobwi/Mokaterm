using Renci.SshNet;
using Renci.SshNet.Messages;

namespace Mokaterm.Modules.Ssh.Forwarding;

/// <summary>Writes through SSH.NET's own send path, so a message is packed, encrypted and ordered with everything else.</summary>
internal sealed class SessionMessageSender : ISshMessageSender
{
	private readonly Session _session;
	private readonly Action<Session, Message> _send;

	public SessionMessageSender(Session session, Action<Session, Message> send)
	{
		_session = session;
		_send = send;
	}

	public bool TrySend(Message message)
	{
		ArgumentNullException.ThrowIfNull(message);
		if (!_session.IsConnected)
		{
			return false;
		}

		try
		{
			_send(_session, message);
			return true;
		}
		catch (Exception)
		{
			// A send fails because the connection is going away, and the session reports that on its own.
			return false;
		}
	}
}
