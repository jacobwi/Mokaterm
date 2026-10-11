using Renci.SshNet.Messages;

namespace Mokaterm.Modules.Ssh.Forwarding;

/// <summary>Writes connection messages for the forwarded channels of one session.</summary>
internal interface ISshMessageSender
{
	/// <summary>Sends <paramref name="message"/>. False when the connection is already gone, which is never an error here.</summary>
	bool TrySend(Message message);
}
