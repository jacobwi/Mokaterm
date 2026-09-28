namespace Mokaterm.Modules.Rdp;

/// <summary>Where the desktop size asked for at connect time comes from.</summary>
public enum RdpDesktopSizeMode
{
	/// <summary>The size of the view when the session opens, which is what most sessions want.</summary>
	Automatic,

	/// <summary>A fixed size saved with the connection, whatever the view measures.</summary>
	Fixed,
}
