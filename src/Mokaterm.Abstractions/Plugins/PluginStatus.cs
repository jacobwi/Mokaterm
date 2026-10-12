namespace Mokaterm.Abstractions.Plugins;

/// <summary>Where an installed plugin stands in this run of the app.</summary>
public enum PluginStatus
{
	/// <summary>Turned on and loaded.</summary>
	Enabled,

	/// <summary>Installed and turned off. Code plugins install like this until the user trusts them.</summary>
	Disabled,

	/// <summary>Turned on, but loading it failed; <see cref="PluginRecord.Problem"/> says why.</summary>
	Failed,

	/// <summary>Turned on, but its files changed since the user trusted them, so it was not loaded.</summary>
	Changed,

	/// <summary>Built for another version of Mokaterm, or not for this host.</summary>
	Incompatible,

	/// <summary>Turned on, but this start skipped every plugin (safe start).</summary>
	Skipped,
}
