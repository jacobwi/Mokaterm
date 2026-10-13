using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.Core.Plugins.Gallery;

/// <summary>
/// The gallery as one window (or circuit) sees it: nothing is asked of the gallery's host until the Browse tab reads the
/// list, which is then kept here until a refresh. A download is checked against the list and handed to the plugin
/// manager, which reads it exactly as it reads a picked file, so the install that follows is the same one.
/// </summary>
internal sealed class PluginGallery : IPluginGallery, IDisposable
{
	private readonly PluginGalleryClient _client;
	private readonly IPluginManager _plugins;
	private readonly string _hostName;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private PluginGalleryIndex? _current;

	public PluginGallery(PluginGalleryClient client, IPluginManager plugins, IAppEnvironment environment)
	{
		ArgumentNullException.ThrowIfNull(client);
		ArgumentNullException.ThrowIfNull(plugins);
		ArgumentNullException.ThrowIfNull(environment);
		_client = client;
		_plugins = plugins;
		_hostName = PluginHosts.For(environment.Kind);
	}

	public event EventHandler? Changed;

	public Uri Index => _client.Index;

	public PluginGalleryIndex? Current => Volatile.Read(ref _current);

	public void Dispose() => _gate.Dispose();

	public async Task<PluginGalleryIndex> ReadIndexAsync(bool refresh, CancellationToken cancellationToken)
	{
		PluginGalleryIndex index;
		await _gate.WaitAsync(cancellationToken);
		try
		{
			// A caller that waited behind a read takes what it read rather than asking the host again.
			if (!refresh && _current is { } kept)
			{
				return kept;
			}

			index = await _client.ReadIndexAsync(_hostName, cancellationToken);
			Volatile.Write(ref _current, index);
		}
		finally
		{
			_gate.Release();
		}

		Changed?.Invoke(this, EventArgs.Empty);
		return index;
	}

	public async Task<IPluginPackage> DownloadAsync(PluginGalleryEntry plugin, PluginGalleryVersion version, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(plugin);
		ArgumentNullException.ThrowIfNull(version);
		if (!plugin.Offered.Contains(version))
		{
			throw new PluginGalleryException(
				$"Mokaterm does not offer {plugin.Name} {version.Version}. {plugin.Unavailable ?? "It was withdrawn, or it is not built for this version."}");
		}

		byte[] data = await _client.DownloadAsync(version, cancellationToken);
		IPluginPackage package;
		try
		{
			using MemoryStream stream = new(data, writable: false);
			package = await _plugins.ReadPackageAsync(stream, new PluginPackageOrigin(plugin.Source, version.Commit, plugin.Verified), cancellationToken);
		}
		catch (InvalidDataException ex)
		{
			throw new PluginGalleryException($"The package from the gallery cannot be installed. {ex.Message}", ex);
		}

		if (Mismatch(plugin, version, package) is { } problem)
		{
			package.Dispose();
			throw new PluginGalleryException(problem);
		}

		return package;
	}

	// The list is what the user chose from, so a package that says it is something else is refused even though its hash
	// matched: the list and the package were made by the same workflow, and a difference means one of them is wrong.
	private static string? Mismatch(PluginGalleryEntry plugin, PluginGalleryVersion version, IPluginPackage package)
	{
		if (!string.Equals(package.Id, plugin.Id, StringComparison.Ordinal) || !string.Equals(package.Version, version.Version, StringComparison.Ordinal))
		{
			return $"The download is {package.Id} {package.Version}, not the {plugin.Id} {version.Version} the gallery lists, so it was not opened.";
		}

		if (package.HasCode != plugin.HasCode)
		{
			return plugin.HasCode
				? $"The gallery lists {plugin.Name} as a plugin that runs code, but the download carries none, so it was not opened."
				: $"The gallery lists {plugin.Name} as data only, but the download carries code, so it was not opened.";
		}

		return null;
	}
}
