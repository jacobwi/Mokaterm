using Mokaterm.UI.Common.Components;

namespace MokatermPlugin1;

/// <summary>The plugin's page in Settings, under Plugins.</summary>
public sealed partial class PluginSettingsPage : SettingsSectionBase<PluginSettings>
{
	// Clamped on the way in too, so a value the fields let through can never leave the ranges the page reads.
	private Task SaveAsync(Func<PluginSettings, PluginSettings> change) => UpdateAsync(settings => change(settings).Clamped());
}
