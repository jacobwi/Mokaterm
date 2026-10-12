using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Abstractions.Fleet;

/// <summary>Which saved logins can report on their machine. The fleet overview lists only those, and the runner refuses the rest with this reason.</summary>
public static class HostSurveyEligibility
{
	/// <summary>Null when a login over <paramref name="protocol"/> can report on its machine; otherwise the reason it cannot.</summary>
	public static string? WhyNot(ProtocolDescriptor? protocol) => protocol switch
	{
		null => CommandRunEligibility.NoModule,
		_ when protocol.Has(ProtocolCapabilities.HostSummary) => null,
		_ => $"{protocol.DisplayName} logins cannot report on their machine.",
	};
}
