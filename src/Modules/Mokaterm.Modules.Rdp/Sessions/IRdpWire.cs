using Mokaterm.Modules.Rdp.Protocol;

namespace Mokaterm.Modules.Rdp.Sessions;

/// <summary>The connection as a session loop uses it. <see cref="RdpTransport"/> is the real one.</summary>
internal interface IRdpWire
{
	/// <summary>Reads whole messages. Only the loop's pump reads.</summary>
	RdpFrameReader Reader { get; }

	/// <summary>Sends bytes to the server. Callers hold the loop's gate, which keeps two writers apart.</summary>
	ValueTask WriteAsync(byte[] bytes, CancellationToken cancellationToken);

	/// <summary>Closes the connection, which makes a read that is waiting fail at once. Safe to call more than once.</summary>
	ValueTask CloseAsync();
}
