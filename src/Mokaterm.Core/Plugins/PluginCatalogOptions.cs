using Mokaterm.Abstractions.Platform;

namespace Mokaterm.Core.Plugins;

/// <summary>How a host opens its plugins folder.</summary>
public sealed record PluginCatalogOptions
{
	public required string Directory { get; init; }

	public required HostKind Host { get; init; }

	/// <summary>
	/// True when the user installs and trusts plugins through the app (the desktop). False when the folder is the
	/// operator's (the web host): whatever is in it is on, nothing is installed through the page, and no trust is asked
	/// for, because putting it there was the operator's decision.
	/// </summary>
	public bool UserManaged { get; init; }

	/// <summary>Start without loading any plugin, as <c>MOKATERM_NO_PLUGINS=1</c> asks.</summary>
	public bool SkipAll { get; init; }

	/// <summary>The environment variable every host reads into <see cref="SkipAll"/>.</summary>
	public const string SkipAllVariable = "MOKATERM_NO_PLUGINS";

	/// <summary>True when <see cref="SkipAllVariable"/> is set to 1 or true.</summary>
	public static bool SkipAllRequested() =>
		Environment.GetEnvironmentVariable(SkipAllVariable) is { } value
		&& (value.Trim() == "1" || value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase));
}
