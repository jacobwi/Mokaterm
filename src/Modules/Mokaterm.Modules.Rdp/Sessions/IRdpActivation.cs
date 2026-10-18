using Mokaterm.Modules.Rdp.Protocol;

namespace Mokaterm.Modules.Rdp.Sessions;

/// <summary>
/// The capability exchange and connection finalization a server runs again after a Deactivate All PDU (MS-RDPBCGR
/// 1.3.1.3). The loop reads and writes for it; this only knows what each step wants and says.
/// </summary>
internal interface IRdpActivation : IDisposable
{
	/// <summary>
	/// True once the server finished. The stage decodes for the new session from then on, and the loop gives it a desktop
	/// of <paramref name="width"/> by <paramref name="height"/>.
	/// </summary>
	bool TryFinish(out int width, out int height);

	/// <summary>How to find the next message the activation waits for, or null when its next step reads nothing.</summary>
	RdpMessageSize? NextInputSize();

	/// <summary>Runs one step on <paramref name="input"/> and returns what to send the server, empty when nothing.</summary>
	byte[] Step(byte[]? input);
}
