namespace Mokaterm.Abstractions.Plugins;

/// <summary>
/// Installed plugins and the changes the Plugins page makes to them. Every change applies at the next start: a loaded
/// assembly cannot be unloaded, and a file it was loaded from stays locked.
/// </summary>
public interface IPluginManager
{
	/// <summary>Where plugins are installed.</summary>
	string Directory { get; }

	/// <summary>False on the web host, where plugins come only from the operator's folder.</summary>
	bool CanInstall { get; }

	/// <summary>True when this start skipped every plugin, because the last one ended while loading them or it was asked to.</summary>
	bool IsSafeStart { get; }

	/// <summary>True once a change is waiting for the next start.</summary>
	bool RestartRequired { get; }

	IReadOnlyList<PluginRecord> Plugins { get; }

	event EventHandler? Changed;

	/// <summary>Reads and checks a package without installing it.</summary>
	/// <param name="package">The <c>.mokaplugin</c> file's bytes.</param>
	/// <param name="origin">Where the bytes came from, for the install dialog; null for a file the user picked.</param>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <exception cref="InvalidDataException">The file is not a usable plugin package; the message says why.</exception>
	Task<IPluginPackage> ReadPackageAsync(Stream package, PluginPackageOrigin? origin, CancellationToken cancellationToken);

	/// <summary>
	/// Installs <paramref name="package"/> for the next start. <paramref name="enable"/> turns it on, which for a code
	/// plugin means the user trusts exactly the files listed in the package.
	/// </summary>
	Task InstallAsync(IPluginPackage package, bool enable, CancellationToken cancellationToken);

	/// <summary>The files the next start would load for an installed plugin, for a trust question to show.</summary>
	/// <exception cref="InvalidOperationException">No plugin with that id is installed.</exception>
	Task<PluginFileSet> ReadFilesAsync(string id, CancellationToken cancellationToken);

	/// <summary>
	/// Turns a plugin on or off for the next start. Turning a code plugin on trusts <paramref name="trustedHash"/>, the
	/// <see cref="PluginFileSet.Hash"/> the user was shown, and refuses when the files no longer match it, so what is
	/// trusted is exactly what was looked at.
	/// </summary>
	/// <exception cref="InvalidDataException">The files changed since <paramref name="trustedHash"/> was read.</exception>
	Task SetEnabledAsync(string id, bool enabled, string? trustedHash, CancellationToken cancellationToken);

	/// <summary>Removes a plugin at the next start.</summary>
	Task RemoveAsync(string id, CancellationToken cancellationToken);
}
