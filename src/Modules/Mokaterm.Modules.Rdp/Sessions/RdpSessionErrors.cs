using System.Net.Sockets;
using Devolutions.IronRdp;
using Mokaterm.Modules.Rdp.Protocol;

namespace Mokaterm.Modules.Rdp.Sessions;

/// <summary>
/// The words for a running session that dropped. Every message here reaches the tab as the reason, so none of them names
/// the server: the tab already does.
/// </summary>
internal static class RdpSessionErrors
{
	/// <summary>The connection broke, or the server sent something the session cannot go on from.</summary>
	public static RdpSessionException Dropped(Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		return exception switch
		{
			RdpSessionException session => session,

			// No Set Error Info and no ultimatum came first, so this is a drop and not the server ending the session.
			EndOfStreamException => new("The server closed the connection without ending the session first.", exception),
			IOException or SocketException => new("The connection to the server was lost.", exception),
			InvalidDataException data => new($"The server sent something this client cannot use: {data.Message}.", exception),
			IronRdpException ironRdp => new($"The session could not read what the server sent: {RdpConnectErrors.Describe(ironRdp)}", exception),
			_ => new("The remote session stopped unexpectedly.", exception),
		};
	}

	/// <summary>
	/// The picture could not be made or handed to the page for a reason other than the page going away. Faulting the
	/// session beats leaving a frozen picture over a connection that still takes every key.
	/// </summary>
	public static RdpSessionException StoppedDrawing(Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		return new RdpSessionException("The remote desktop could not be drawn any more, so the session was closed.", exception);
	}
}
