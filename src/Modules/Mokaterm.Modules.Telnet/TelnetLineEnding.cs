namespace Mokaterm.Modules.Telnet;

/// <summary>What the Enter key sends. RFC 854 spells a new line CR LF and a bare carriage return CR NUL.</summary>
public enum TelnetLineEnding
{
	/// <summary>CR LF, the new line of RFC 854. What almost every telnet server expects.</summary>
	CrLf,

	/// <summary>CR NUL: a carriage return with no line feed, which some line oriented devices want.</summary>
	CrNul,

	/// <summary>LF alone, for terminal servers and consoles that treat CR as a stray character.</summary>
	Lf,
}
