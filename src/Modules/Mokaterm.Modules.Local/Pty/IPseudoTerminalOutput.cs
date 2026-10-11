namespace Mokaterm.Modules.Local.Pty;

/// <summary>Where a pseudo terminal puts what its program writes.</summary>
internal interface IPseudoTerminalOutput
{
	/// <summary>
	/// Takes one chunk of output on the terminal's own reader thread, and may block that thread while the view is behind,
	/// which is the back pressure a pty gives its program. Returns false once no more output is wanted; the terminal
	/// still reads the rest and drops it, because a console with nobody reading cannot close.
	/// </summary>
	bool Write(ReadOnlySpan<byte> chunk);
}
