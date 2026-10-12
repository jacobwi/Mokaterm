using Microsoft.Extensions.FileProviders;
using Mokaterm.Abstractions.Plugins;
using Mokaterm.Core.Plugins;

namespace Mokaterm.Web.Services;

/// <summary>
/// Serves each loaded plugin's <c>wwwroot</c> at the <c>_content</c> path a built-in module's would have. The build's
/// own static asset endpoints know nothing about a plugin, so this runs ahead of them. Linked into the DevHost too.
/// </summary>
internal static class PluginFiles
{
	public static WebApplication UsePluginFiles(this WebApplication app, PluginCatalog catalog)
	{
		ArgumentNullException.ThrowIfNull(app);
		ArgumentNullException.ThrowIfNull(catalog);
		foreach (LoadedPlugin plugin in catalog.Loaded)
		{
			PluginInfo info = plugin.Info;
			if (info.WebRoot is { } webRoot)
			{
				app.UseStaticFiles(new StaticFileOptions
				{
					FileProvider = new PhysicalFileProvider(webRoot),
					RequestPath = "/" + info.ContentPath.TrimEnd('/'),
				});
			}
		}

		return app;
	}
}
