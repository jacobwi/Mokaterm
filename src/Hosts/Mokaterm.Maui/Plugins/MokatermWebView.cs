using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.FileProviders;
using Mokaterm.Abstractions.Plugins;

namespace Mokaterm.Maui.Plugins;

/// <summary>
/// The app's BlazorWebView, for the main window and every tool window: it also serves each plugin's files at the
/// <c>_content</c> path a built-in module's would have.
/// </summary>
internal class MokatermWebView : BlazorWebView
{
	public override IFileProvider CreateFileProvider(string contentRootDir)
	{
		IFileProvider app = base.CreateFileProvider(contentRootDir);
		PluginInfo[] plugins = [.. IPlatformApplication.Current?.Services.GetServices<PluginInfo>() ?? []];
		return plugins.Any(plugin => plugin.WebRoot is not null) ? new PluginFileProvider(app, plugins) : app;
	}
}
