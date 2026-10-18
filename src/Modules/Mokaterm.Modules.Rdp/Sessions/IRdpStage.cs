using Mokaterm.Modules.Rdp.Protocol;
using Action = Devolutions.IronRdp.Action;

namespace Mokaterm.Modules.Rdp.Sessions;

/// <summary>
/// Everything of a running session that is an IronRDP handle: the active stage, the decoded desktop and the input state.
/// None of it is thread safe and freeing any of it during a call is a use after free, so the loop calls it only while
/// holding its gate. Outputs come back in the order IronRDP wants them handled.
/// </summary>
internal interface IRdpStage : IDisposable
{
	/// <summary>The MCS channel the server sends the session's own PDUs on, where it says why it ends a session.</summary>
	int IoChannelId { get; }

	/// <summary>Decodes one message from the server into the desktop.</summary>
	void Process(Action action, byte[] payload, List<RdpStageOutput> outputs);

	/// <summary>Encodes one input operation for the server.</summary>
	void ApplyInput(RdpOperation operation, List<RdpStageOutput> outputs);

	/// <summary>Encodes a request for a desktop of this size, or returns false when the server offered no channel for it.</summary>
	bool TryEncodeResize(int width, int height, List<RdpStageOutput> outputs);

	/// <summary>
	/// Copies the whole decoded desktop into <paramref name="buffer"/> when it is exactly the desktop's size, otherwise
	/// into a new array, and returns the one it filled.
	/// </summary>
	byte[] CopyDesktop(byte[] buffer);

	/// <summary>Replaces the decoded desktop with an empty one of this size, after the server activated the session again.</summary>
	void ResetDesktop(int width, int height);
}
