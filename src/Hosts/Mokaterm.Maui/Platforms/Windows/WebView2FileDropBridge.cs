using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Maui.Services;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Maui.Platforms.Windows;

/// <summary>
/// File drops through WebView2: the page script forwards the dropped File objects to the host with
/// postMessageWithAdditionalObjects, which exposes their real paths, so uploads stream from disk and dropped
/// folders keep their structure.
/// </summary>
internal sealed class WebView2FileDropBridge : IFileDropBridge, IAsyncDisposable
{
	// The native message normally lands within milliseconds of the Blazor call; this only guards against a lost one.
	private static readonly TimeSpan PathsTimeout = TimeSpan.FromSeconds(10);

	private readonly JsModule _module;
	private readonly DesktopDropCoordinator _coordinator;
	private readonly ILogger<WebView2FileDropBridge> _logger;

	public WebView2FileDropBridge(IJSRuntime jsRuntime, DesktopDropCoordinator coordinator, ILogger<WebView2FileDropBridge> logger)
	{
		_module = new JsModule(jsRuntime, "./js/desktop-drop.js");
		_coordinator = coordinator;
		_logger = logger;
	}

	public async ValueTask<IAsyncDisposable> AttachAsync(ElementReference element, FileDropHandlers handlers, CancellationToken cancellationToken = default)
	{
		Registration registration = new(this, handlers);
		registration.Reference = DotNetObjectReference.Create(registration);
		registration.Listener = await _module.InvokeAsync<IJSObjectReference>("attach", cancellationToken, element, registration.Reference, DesktopDropCoordinator.MessagePrefix);
		return registration;
	}

	public ValueTask DisposeAsync() => _module.DisposeAsync();

	internal sealed class Registration : IAsyncDisposable
	{
		private readonly WebView2FileDropBridge _owner;
		private readonly FileDropHandlers _handlers;

		public Registration(WebView2FileDropBridge owner, FileDropHandlers handlers)
		{
			_owner = owner;
			_handlers = handlers;
		}

		public DotNetObjectReference<Registration>? Reference { get; set; }

		public IJSObjectReference? Listener { get; set; }

		[JSInvokable]
		public Task OnDragOver(bool isOver) => _handlers.OnDragOver?.Invoke(isOver) ?? Task.CompletedTask;

		[JSInvokable]
		public async Task OnDrop(string dropId, bool ctrlKey, bool shiftKey, bool altKey)
		{
			try
			{
				IReadOnlyList<string> paths = await _owner._coordinator.WaitForPathsAsync(dropId, PathsTimeout, CancellationToken.None);
				IReadOnlyList<LocalFileItem> items = await Task.Run(() => LocalPaths.Expand(paths));
				if (items.Count > 0)
				{
					await _handlers.OnDrop(new FileDropEvent(items, ctrlKey, shiftKey, altKey));
				}
			}
			catch (Exception ex)
			{
				// A drop that fails is the user's file, not the page's problem: letting it out would cross back into
				// JavaScript as an interop error and leave the drop overlay behind.
				_owner._logger.LogError(ex, "Handling dropped files failed");
			}
		}

		public async ValueTask DisposeAsync()
		{
			// Releasing the listener cannot be skipped over a script that throws: the reference below is a .NET object
			// the page holds, and it would stay alive for the window's lifetime.
			await Listener.ReleaseAsync();
			Reference?.Dispose();
		}
	}
}
