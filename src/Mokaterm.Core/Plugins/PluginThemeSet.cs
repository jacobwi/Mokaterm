namespace Mokaterm.Core.Plugins;

/// <summary>The theme files of every plugin this start enabled, for the terminal theme catalog.</summary>
internal sealed class PluginThemeSet(IReadOnlyList<PluginThemeFile> files)
{
	public IReadOnlyList<PluginThemeFile> Files { get; } = files;
}
