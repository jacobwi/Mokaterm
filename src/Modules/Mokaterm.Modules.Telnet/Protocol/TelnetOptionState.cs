namespace Mokaterm.Modules.Telnet.Protocol;

/// <summary>
/// One side's state for one option, as RFC 1143 defines it. The Opposite states remember a change of mind that
/// arrived while an earlier request was still unanswered, which is what keeps two sides out of a negotiation loop.
/// </summary>
internal enum TelnetOptionState
{
	No,
	Yes,
	WantNo,
	WantNoOpposite,
	WantYes,
	WantYesOpposite,
}
