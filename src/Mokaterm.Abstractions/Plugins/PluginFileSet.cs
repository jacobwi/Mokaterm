namespace Mokaterm.Abstractions.Plugins;

/// <summary>
/// The files of an installed plugin as the next start would load them, and their combined hash: what a trust question
/// shows, and what turning the plugin on then trusts.
/// </summary>
public sealed record PluginFileSet(IReadOnlyList<PluginPackageFile> Files, string Hash);
