using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.Maui.Plugins;

/// <summary>The app's own files, with each plugin's <c>wwwroot</c> answering under its <see cref="PluginInfo.ContentPath"/>.</summary>
internal sealed class PluginFileProvider : IFileProvider, IDisposable
{
	private readonly IFileProvider _app;
	private readonly List<(string Prefix, PhysicalFileProvider Files)> _plugins = [];

	public PluginFileProvider(IFileProvider app, IEnumerable<PluginInfo> plugins)
	{
		ArgumentNullException.ThrowIfNull(app);
		ArgumentNullException.ThrowIfNull(plugins);
		_app = app;
		foreach (PluginInfo plugin in plugins)
		{
			if (plugin.WebRoot is { } root)
			{
				_plugins.Add((plugin.ContentPath, new PhysicalFileProvider(root)));
			}
		}
	}

	public IFileInfo GetFileInfo(string subpath)
	{
		ArgumentNullException.ThrowIfNull(subpath);
		string path = subpath.TrimStart('/');
		foreach ((string prefix, PhysicalFileProvider files) in _plugins)
		{
			// PhysicalFileProvider refuses anything that would leave its root, so a "../" cannot reach past the plugin.
			if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				return files.GetFileInfo(path[prefix.Length..]);
			}
		}

		return _app.GetFileInfo(subpath);
	}

	public IDirectoryContents GetDirectoryContents(string subpath) => _app.GetDirectoryContents(subpath);

	public IChangeToken Watch(string filter) => _app.Watch(filter);

	public void Dispose()
	{
		foreach ((_, PhysicalFileProvider files) in _plugins)
		{
			files.Dispose();
		}
	}
}
