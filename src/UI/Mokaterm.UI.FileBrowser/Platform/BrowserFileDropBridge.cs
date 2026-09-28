using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.FileBrowser.Platform;

/// <summary>
/// File drops read in the page: folders are walked with <c>webkitGetAsEntry</c>, and every file streams to .NET through
/// an <see cref="IJSStreamReference"/> when its upload starts. Works on Blazor Server. Inside a MAUI BlazorWebView the
/// desktop host can replace it with a bridge that reads dropped paths from disk.
/// </summary>
internal sealed class BrowserFileDropBridge : IFileDropBridge, IAsyncDisposable
{
	private const string ModulePath = "./_content/Mokaterm.UI.FileBrowser/js/filedrop.js";

	private readonly JsModule _module;
	private readonly ILogger<BrowserFileDropBridge> _logger;

	public BrowserFileDropBridge(IJSRuntime jsRuntime, ILogger<BrowserFileDropBridge> logger)
	{
		_module = new JsModule(jsRuntime, ModulePath);
		_logger = logger;
	}

	public async ValueTask<IAsyncDisposable> AttachAsync(ElementReference element, FileDropHandlers handlers, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(handlers);

		BrowserFileDropRegistration registration = new(_module, handlers, _logger);
		try
		{
			await registration.AttachAsync(element, cancellationToken);
			return registration;
		}
		catch
		{
			await registration.DisposeAsync();
			throw;
		}
	}

	public ValueTask DisposeAsync() => _module.DisposeAsync();
}
