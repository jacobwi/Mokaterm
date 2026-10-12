using Mokaterm.Abstractions.Settings;

namespace MokatermPlugin1;

/// <summary>
/// The plugin's settings, kept in Mokaterm's settings.json and carried by its settings backup. The key is
/// <c>plugin.</c> and the plugin's id (MokatermPluginId in the project file): the loader refuses a plugin whose
/// settings use any other name, so they can never land on one of Mokaterm's own sections.
/// </summary>
public sealed record PluginSettings : ISettingsSection
{
	public const int MinRefreshSeconds = 5;

	public const int MaxRefreshSeconds = 3600;

	public const int MaxParameters = 32;

	public const string DefaultParameters = "vm.swappiness vm.overcommit_memory net.ipv4.ip_forward net.core.somaxconn fs.file-max kernel.pid_max";

	public static string SectionKey => "plugin.PLUGIN_ID";

	/// <summary>The kernel parameters the page reads, by their sysctl names, separated by spaces.</summary>
	public string Parameters { get; init; } = DefaultParameters;

	/// <summary>How often the page reads them again while it is shown.</summary>
	public int RefreshSeconds { get; init; } = 30;

	/// <summary>A copy with every value inside what the settings page allows, since settings.json can be edited by hand.</summary>
	public PluginSettings Clamped() => this with
	{
		Parameters = ParametersProblem(Parameters) is null ? Parameters.Trim() : DefaultParameters,
		RefreshSeconds = Math.Clamp(RefreshSeconds, MinRefreshSeconds, MaxRefreshSeconds),
	};

	/// <summary>The parameter names in <paramref name="parameters"/>, each once, in the order given.</summary>
	public static IReadOnlyList<string> Split(string? parameters) =>
		[.. (parameters ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal)];

	/// <summary>Why <paramref name="parameters"/> cannot be saved, or null. An empty list is allowed: the page then reads nothing.</summary>
	public static string? ParametersProblem(string? parameters)
	{
		if (parameters is null)
		{
			return "The parameter list is missing.";
		}

		IReadOnlyList<string> names = Split(parameters);
		if (names.Count > MaxParameters)
		{
			return $"Name at most {MaxParameters} parameters.";
		}

		return names.Select(KernelParameterCommands.NameProblem).FirstOrDefault(problem => problem is not null);
	}
}
