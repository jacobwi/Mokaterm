using Moka.Red.Core.Enums;
using Mokaterm.Abstractions.Connections;

namespace Mokaterm.UI.Presentation;

/// <summary>How a host environment looks in tags, tabs and the status bar.</summary>
internal static class HostEnvironmentDisplay
{
	public static IReadOnlyList<HostEnvironment> All { get; } =
		[HostEnvironment.None, HostEnvironment.Development, HostEnvironment.Staging, HostEnvironment.Production];

	/// <summary>Uppercase tag text, or null for <see cref="HostEnvironment.None"/>.</summary>
	public static string? Tag(HostEnvironment environment) => environment switch
	{
		HostEnvironment.Development => "DEV",
		HostEnvironment.Staging => "STAGING",
		HostEnvironment.Production => "PRODUCTION",
		_ => null,
	};

	public static string Name(HostEnvironment environment) => environment switch
	{
		HostEnvironment.Development => "Development",
		HostEnvironment.Staging => "Staging",
		HostEnvironment.Production => "Production",
		_ => "None",
	};

	public static MokaColor Color(HostEnvironment environment) => environment switch
	{
		HostEnvironment.Development => MokaColor.Info,
		HostEnvironment.Staging => MokaColor.Warning,
		HostEnvironment.Production => MokaColor.Error,
		_ => MokaColor.Surface,
	};
}
