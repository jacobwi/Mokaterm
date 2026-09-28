namespace Mokaterm.Modules.Telnet;

/// <summary>Who puts typed characters on the screen.</summary>
public enum TelnetEchoMode
{
	/// <summary>Echo locally until the server turns the ECHO option on, which is what the protocol intends.</summary>
	Auto,

	/// <summary>Always echo locally, for devices that never echo and never negotiate.</summary>
	On,

	/// <summary>Never echo locally, for devices that echo without negotiating and would otherwise double every character.</summary>
	Off,
}
