namespace Mokaterm.Modules.Serial.Components;

/// <summary>What the line view is waiting on the port for.</summary>
internal enum SerialLineAction
{
	Refresh,
	Break,
	Dtr,
	Rts,
}
