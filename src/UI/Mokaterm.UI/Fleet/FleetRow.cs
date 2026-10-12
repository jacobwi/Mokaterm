using Mokaterm.Abstractions.Connections;
using Mokaterm.Abstractions.Fleet;

namespace Mokaterm.UI.Fleet;

/// <summary>
/// One login of the fleet overview: the login and its machine as the catalog has them, and what the window last read
/// from it, if anything. Public because component parameters are, and a class rather than a record because it holds
/// host and user names that a record would print.
/// </summary>
public sealed class FleetRow
{
	internal FleetRow(HostProfile host, ConnectionProfile login, HostSurveyResult? result)
	{
		Host = host;
		Login = login;
		Result = result;
	}

	public HostProfile Host { get; }

	public ConnectionProfile Login { get; }

	/// <summary>The survey's row for this login, or null before the window read it.</summary>
	public HostSurveyResult? Result { get; }

	public HostSummary? Summary => Result?.Summary;

	public bool IsPending => Result?.IsPending == true;
}
