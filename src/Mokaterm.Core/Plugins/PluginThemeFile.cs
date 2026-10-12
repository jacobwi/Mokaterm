namespace Mokaterm.Core.Plugins;

/// <summary>A terminal theme file from an enabled plugin's <c>themes</c> folder, read at start.</summary>
internal sealed record PluginThemeFile(string PluginId, string FileName, string Text);
