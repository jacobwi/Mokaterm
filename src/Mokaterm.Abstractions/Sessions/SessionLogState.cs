namespace Mokaterm.Abstractions.Sessions;

public enum SessionLogState
{
	/// <summary>Nothing is written for this session.</summary>
	Off,

	Recording,

	/// <summary>The size cap was reached. The file says so on its last line and takes nothing more.</summary>
	Full,

	/// <summary>Writing stopped on an error, for example a folder this account cannot write to. The session is unaffected.</summary>
	Failed,
}
