using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.FileBrowser.Interop;
using Mokaterm.UI.FileBrowser.Platform;
using Mokaterm.UI.FileBrowser.Transfers;

namespace Mokaterm.UI.FileBrowser.Extensions;

public static class FileBrowserServiceCollectionExtensions
{
	/// <summary>
	/// Registers the file browser services, including a browser-based <c>IFileDropBridge</c> hosts may replace.
	/// Called by the shell's <c>AddMokatermUI()</c>; safe to call twice.
	/// </summary>
	public static IServiceCollection AddMokatermFileBrowser(this IServiceCollection services)
	{
		services.TryAddSingleton(TimeProvider.System);
		services.TryAddScoped<IFileDropBridge, BrowserFileDropBridge>();
		services.TryAddScoped<FileBrowserInterop>();
		services.TryAddScoped<RemoteTransferService>();
		return services;
	}
}
