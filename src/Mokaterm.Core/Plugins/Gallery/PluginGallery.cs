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
	private readonly object _downloadLock = new();
	private PluginGalleryIndex? _current;
	private PluginGalleryDownload? _download;

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

	public PluginGalleryDownload? Download
	{
		get
		{
			lock (_downloadLock)
			{
				return _download;
			}
		}
	}

	public void Dispose()
	{
		_gate.Dispose();
		lock (_downloadLock)
		{
			(_download?.Package as IDisposable)?.Dispose();
			_download = null;
		}
	}

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

	public async Task BeginDownloadAsync(PluginGalleryEntry plugin, PluginGalleryVersion version)
	{
		ArgumentNullException.ThrowIfNull(plugin);
		ArgumentNullException.ThrowIfNull(version);
		lock (_downloadLock)
		{
			if (_download is { Done: false })
			{
				// A download is already running; the page's busy state should have kept a second start out.
				return;
			}

			// A package nobody took from the last download goes; the new one replaces what the window shows.
			(_download?.Package as IDisposable)?.Dispose();
			_download = new PluginGalleryDownload
			{
				PluginId = plugin.Id,
				PluginName = plugin.Name,
				Version = version.Version,
			};
		}

		Changed?.Invoke(this, EventArgs.Empty);
		try
		{
			IPluginPackage package = await DownloadAsync(plugin, version, CancellationToken.None);
			lock (_downloadLock)
			{
				_download = _download is null ? null : _download with { Done = true, Package = package };
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			lock (_downloadLock)
			{
				_download = _download is null ? null : _download with
				{
					Done = true,
					Problem = ex is PluginGalleryException gallery ? gallery.Message : "Try again in a moment.",
				};
			}
		}

		Changed?.Invoke(this, EventArgs.Empty);
	}

	public IPluginPackage? TakeDownload()
	{
		lock (_downloadLock)
		{
			// A download still running, or one that failed, has nothing to take and keeps its place.
			if (_download?.Package is not { } package)
			{
				return null;
			}

			_download = null;
			return package;
		}
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
