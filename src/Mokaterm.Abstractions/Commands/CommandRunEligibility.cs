using Mokaterm.Abstractions.Protocols;

namespace Mokaterm.Abstractions.Commands;

/// <summary>Which saved logins a command run can reach. The run dialog lists the rest with this reason, and the runner refuses them with it.</summary>
public static class CommandRunEligibility
{
	/// <summary>The reason for a login whose protocol no loaded module offers, whatever the run wanted from it.</summary>
	public const string NoModule = "No module here offers this login's protocol.";

	/// <summary>Null when a login over <paramref name="protocol"/> can run a command; otherwise the reason it cannot.</summary>
	public static string? WhyNot(ProtocolDescriptor? protocol) => protocol switch
	{
		null => NoModule,
		_ when protocol.Has(ProtocolCapabilities.Commands) => null,
		_ => $"{protocol.DisplayName} logins cannot run a command without a terminal.",
	};
}
