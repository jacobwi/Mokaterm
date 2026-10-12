namespace Mokaterm.Abstractions.Plugins;

/// <summary>One file in a plugin package: its path inside the package, its size and its SHA-256 in lower-case hex.</summary>
public sealed record PluginPackageFile(string Path, long Length, string Sha256);
