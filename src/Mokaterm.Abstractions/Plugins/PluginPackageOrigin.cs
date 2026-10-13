namespace Mokaterm.Abstractions.Plugins;

/// <summary>
/// Where a package came from when it was not a file the user picked: the gallery, and the public source it was built
/// from. The install dialog shows it, because "built from this commit" is what the gallery vouches for.
/// </summary>
/// <param name="Source">The https address of the repository the package was built from.</param>
/// <param name="Commit">The full commit in <paramref name="Source"/> the package was built from.</param>
/// <param name="Verified">True for the gallery's own plugins, whose source lives in the gallery repository.</param>
public sealed record PluginPackageOrigin(Uri Source, string Commit, bool Verified);
