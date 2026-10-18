namespace Mokaterm.Modules.Rdp.Sessions;

/// <summary>What one <see cref="RdpStageOutput"/> asks the session loop to do. The cases of IronRDP's <c>ActiveStageOutput</c>.</summary>
internal enum RdpStageOutputKind
{
	/// <summary>Bytes to send the server.</summary>
	ResponseFrame,

	/// <summary>A part of the desktop was redrawn.</summary>
	GraphicsUpdate,

	/// <summary>The server drew a pointer.</summary>
	PointerBitmap,

	/// <summary>The server moved the pointer itself.</summary>
	PointerPosition,

	PointerHidden,

	PointerDefault,

	/// <summary>IronRDP ended the session.</summary>
	Terminate,

	/// <summary>The server deactivated the session to activate it again, which is what a resize looks like.</summary>
	DeactivateAll,
}
