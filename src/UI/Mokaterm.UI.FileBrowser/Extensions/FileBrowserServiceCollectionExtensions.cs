using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mokaterm.UI.Common.Editing;
using Mokaterm.UI.Common.Files;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.FileBrowser.Bookmarks;
using Mokaterm.UI.FileBrowser.Dialogs;
using Mokaterm.UI.FileBrowser.Editing;
using Mokaterm.UI.FileBrowser.Interop;
using Mokaterm.UI.FileBrowser.Platform;
using Mokaterm.UI.FileBrowser.Transfers;

namespace Mokaterm.UI.FileBrowser.Extensions;

public static class FileBrowserServiceCollectionExtensions
{
	/// <summary>
	/// Registers the file browser services, including a browser-based <c>IFileDropBridge</c> hosts may replace, the text
	/// editor that modules reach through <see cref="IRemoteTextEditor"/>, and the browser itself in a dialog, which they
	/// reach through <see cref="IFileBrowserDialog"/>. Called by the shell's <c>AddMokatermUI()</c>; safe to call twice.
	/// </summary>
	public static IServiceCollection AddMokatermFileBrowser(this IServiceCollection services)
	{
		services.TryAddSingleton(TimeProvider.System);
		services.TryAddScoped<IFileDropBridge, BrowserFileDropBridge>();
		services.TryAddScoped<FileBrowserInterop>();
		services.TryAddScoped<RemoteTransferService>();
		services.TryAddScoped<FolderBookmarkStore>();
		services.TryAddScoped<IRemoteTextEditor, RemoteTextEditor>();
		services.TryAddScoped<IFileBrowserDialog, FileBrowserDialogService>();
		return services;
	}
}
