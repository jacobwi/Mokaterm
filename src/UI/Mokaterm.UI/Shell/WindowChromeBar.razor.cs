using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.Shell;

/// <summary>
/// Makes its content the title bar of the window this page is in. It puts the size of the window's own buttons in the
/// <c>--mt-chrome-left</c>, <c>--mt-chrome-right</c> and <c>--mt-chrome-height</c> properties of its element, for the
/// content to keep that stretch empty and match their height, and hands the content's empty stretches to the window as
/// the parts that move it. Every window has its own <see cref="IWindowChrome"/>, so a tool window's bar moves the tool
/// window.
/// </summary>
public sealed partial class WindowChromeBar : ComponentBase, IAsyncDisposable
{
	private const string ModulePath = "./_content/Mokaterm.UI/js/window-chrome.js";

	private IWindowChrome? _chrome;
	private string? _style;
	private ElementReference _bar;
	private JsModule? _module;
	private IJSObjectReference? _tracker;
	private DragRegionReceiver? _receiver;
	private DotNetObjectReference<DragRegionReceiver>? _reference;
	private bool _disposed;

	/// <summary>The class of the element around the content, which the content's styles hang off.</summary>
	[Parameter]
	public string? Class { get; set; }

	[Parameter]
	public RenderFragment? ChildContent { get; set; }

	[Inject]
	private IJSRuntime JSRuntime { get; set; } = default!;

	// Optional: only a host whose windows have no title bar of their own registers IWindowChrome.
	[Inject]
	private IServiceProvider Services { get; set; } = default!;

	public async ValueTask DisposeAsync()
	{
		_disposed = true;
		await DetachAsync();
	}

	protected override void OnInitialized()
	{
		_chrome = Services.GetService<IWindowChrome>();
		if (_chrome is not null)
		{
			_style = Style(_chrome.Insets);
			_chrome.InsetsChanged += OnInsetsChanged;
		}
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (firstRender)
		{
			await AttachAsync();
		}
	}

	// Rounded up: a bar that stops a fraction of a pixel short of the window's buttons would slide under them.
	private static string Style(WindowChromeInsets insets) => string.Create(
		CultureInfo.InvariantCulture,
		$"--mt-chrome-left:{Math.Ceiling(insets.Left)}px;--mt-chrome-right:{Math.Ceiling(insets.Right)}px;--mt-chrome-height:{Math.Ceiling(insets.Height)}px");

	private static async Task StopTrackerAsync(IJSObjectReference tracker)
	{
		try
		{
			await tracker.InvokeVoidAsync("dispose");
			await tracker.DisposeAsync();
		}
		catch (Exception ex) when (JsModule.IsTeardown(ex))
		{
			// The window is closing; its observers go with it.
		}
	}

	private async Task AttachAsync()
	{
		if (_chrome is null || _disposed)
		{
			return;
		}

		_module = new JsModule(JSRuntime, ModulePath);
		_receiver = new DragRegionReceiver(_chrome);
		_reference = DotNetObjectReference.Create(_receiver);
		try
		{
			_tracker = await _module.InvokeAsync<IJSObjectReference>("attach", _bar, _reference);
		}
		catch (Exception ex) when (JsModule.IsTeardown(ex))
		{
			// The window closed, or the vault locked, while the script was attaching.
			return;
		}

		// The bar went away while the script was attaching, so the detach found no tracker to stop.
		if (_disposed)
		{
			await DetachAsync();
		}
	}

	private async Task DetachAsync()
	{
		if (_chrome is null)
		{
			return;
		}

		_chrome.InsetsChanged -= OnInsetsChanged;

		// A report still on its way from the page must not land after the reset below.
		_receiver?.Stop();

		// The page shows no bar now (the lock screen, a closing tool window): the window gets its own title bar strip
		// back so it can still be moved.
		_chrome.ResetDragRegions();

		IJSObjectReference? tracker = _tracker;
		DotNetObjectReference<DragRegionReceiver>? reference = _reference;
		JsModule? module = _module;
		_tracker = null;
		_reference = null;
		_module = null;

		if (tracker is not null)
		{
			await StopTrackerAsync(tracker);
		}

		reference?.Dispose();
		if (module is not null)
		{
			await module.DisposeAsync();
		}
	}

	private void OnInsetsChanged() => _ = InvokeAsync(() =>
	{
		if (_chrome is not null && !_disposed)
		{
			_style = Style(_chrome.Insets);
			StateHasChanged();
		}
	});

	/// <summary>Receives the bar's empty stretches from <c>window-chrome.js</c> and hands them to the window.</summary>
	internal sealed class DragRegionReceiver
	{
		private readonly IWindowChrome _chrome;
		private bool _stopped;

		public DragRegionReceiver(IWindowChrome chrome) => _chrome = chrome;

		[JSInvokable]
		public void OnDragRegionsChanged(WindowDragRegion[] regions)
		{
			if (!_stopped)
			{
				_chrome.SetDragRegions(regions);
			}
		}

		public void Stop() => _stopped = true;
	}
}
