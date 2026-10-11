using System.Reflection;
using System.Text;
using Mokaterm.Modules.Ssh.Agent;
using Mokaterm.Modules.Ssh.Forwarding;
using Renci.SshNet;
using Renci.SshNet.Messages;
using Renci.SshNet.Messages.Connection;

namespace Mokaterm.Modules.Ssh.Tests.Fakes;

/// <summary>
/// A real SSH.NET <see cref="Session"/> that never connects, with messages pushed into it the way its message loop
/// pushes what a server sent. Forwarding then meets SSH.NET's own events and message classes rather than copies of
/// them, so a change in either fails here.
/// </summary>
internal static class SshNetSession
{
	private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

	public static Session Create()
	{
		Assembly sshNet = typeof(Session).Assembly;
		object services = Activator.CreateInstance(sshNet.GetType("Renci.SshNet.ServiceFactory", throwOnError: true)!, nonPublic: true)!;
		object sockets = Activator.CreateInstance(sshNet.GetType("Renci.SshNet.Connection.SocketFactory", throwOnError: true)!, nonPublic: true)!;
		ConnectionInfo info = new("127.0.0.1", 22, "tester", new NoneAuthenticationMethod("tester"));
		ConstructorInfo constructor = typeof(Session).GetConstructors(Hidden).Single(candidate => candidate.GetParameters().Length == 3);
		return (Session)constructor.Invoke([info, services, sockets]);
	}

	/// <summary>Hands <paramref name="message"/> to the session as if the server had sent it.</summary>
	public static void Receive(this Session session, Message message)
	{
		(string handler, Type parameter) = message switch
		{
			ChannelOpenMessage => ("OnChannelOpenReceived", typeof(ChannelOpenMessage)),
			ChannelOpenConfirmationMessage => ("OnChannelOpenConfirmationReceived", typeof(ChannelOpenConfirmationMessage)),
			ChannelDataMessage => ("OnChannelDataReceived", typeof(ChannelDataMessage)),
			ChannelWindowAdjustMessage => ("OnChannelWindowAdjustReceived", typeof(ChannelWindowAdjustMessage)),
			ChannelEofMessage => ("OnChannelEofReceived", typeof(ChannelEofMessage)),
			ChannelCloseMessage => ("OnChannelCloseReceived", typeof(ChannelCloseMessage)),
			ChannelRequestMessage => ("OnChannelRequestReceived", typeof(ChannelRequestMessage)),
			_ => throw new ArgumentException($"No handler for {message.GetType().Name}.", nameof(message)),
		};

		MethodInfo method = typeof(Session).GetMethod(handler, Hidden, [parameter])
			?? throw new InvalidOperationException($"SSH.NET no longer has Session.{handler}.");
		_ = method.Invoke(session, [message]);
	}

	/// <summary>An <c>x11</c> channel open as OpenSSH's server sends it.</summary>
	public static ChannelOpenMessage X11Open(uint sender, uint window = 1024 * 1024, uint packet = 32 * 1024)
	{
		ChannelOpenMessage message = new();
		message.Load(OpenBytes(SshForwarding.X11ChannelType, sender, window, packet, new SshWireWriter().WriteString("127.0.0.1"u8).WriteUInt32(40000).ToArray()));
		return message;
	}

	/// <summary>An <c>auth-agent@openssh.com</c> channel open, read the way the changed message table reads it.</summary>
	public static ChannelOpenMessage AgentOpen(uint sender, uint window = 64 * 1024, uint packet = 32 * 1024)
	{
		TolerantChannelOpenMessage message = new();
		message.Load(OpenBytes(SshForwarding.AgentChannelType, sender, window, packet, []));
		return message;
	}

	/// <summary>The body of a channel open message (no message number), for <see cref="Renci.SshNet.Common.SshData.Load(byte[])"/>.</summary>
	public static byte[] OpenBytes(string type, uint sender, uint window, uint packet, byte[] info) =>
		[
			.. new SshWireWriter()
				.WriteString(Encoding.ASCII.GetBytes(type))
				.WriteUInt32(sender)
				.WriteUInt32(window)
				.WriteUInt32(packet)
				.ToArray(),
			.. info,
		];
}
