namespace Mokaterm.Modules.Rdp;

/// <summary>How the remote screen is fitted into the session view.</summary>
public enum RdpScalingMode
{
	/// <summary>Scales the remote screen down or up so all of it is visible.</summary>
	Fit,

	/// <summary>One remote pixel per local pixel, with scrollbars when the screen is larger than the view.</summary>
	Actual,

	/// <summary>Asks the server to resize its desktop to the view. Falls back to 1:1 when the server refuses.</summary>
	Remote,
}
