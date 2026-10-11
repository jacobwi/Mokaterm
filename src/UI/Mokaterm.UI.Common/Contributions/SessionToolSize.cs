namespace Mokaterm.UI.Common.Contributions;

/// <summary>How much room a session tool asks for when the shell opens it in a dialog.</summary>
public enum SessionToolSize
{
	/// <summary>A form-sized dialog that grows with its content, such as the tunnels panel.</summary>
	Standard,

	/// <summary>
	/// Nearly the whole window and a fixed height, for a list with a detail panel beside it. The view fills that height
	/// and scrolls its own parts.
	/// </summary>
	Wide,
}
