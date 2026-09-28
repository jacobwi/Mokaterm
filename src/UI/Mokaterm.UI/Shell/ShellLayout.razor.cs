using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Transfers;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Connections;

namespace Mokaterm.UI.Shell;

/// <summary>
/// The unlocked application: app bar, connections panel, transfers panel, workspace, status bar and editors. Mounted
/// only while the vault is unlocked, so locking removes every piece of decrypted data from the DOM.
/// </summary>
public sealed partial class ShellLayout : ComponentBase, IAsyncDisposable
{
	private const string WindowChromeModulePath = "./_content/Mokaterm.UI/js/window-chrome.js";

	private bool _showStatusBar;
	private int _lastActiveTransfers;
	private IWindowChrome? _chrome;
	private string? _chromeStyle;
	private ElementReference _topBar;
	private JsModule? _chromeModule;
	private IJSObjectReference? _chromeTracker;
	private DragRegionReceiver? _dragRegions;
	private DotNetObjectReference<DragRegionReceiver>? _dragRegionsReference;
	private bool _disposed;

	[Inject]
	private ShellState Shell { get; set; } = default!;

	[Inject]
	private ShellCommandRegistry Commands { get; set; } = default!;

	[Inject]
	private ShellInputBridge InputBridge { get; set; } = default!;

	[Inject]
	private ConnectionActions Actions { get; set; } = default!;

	[Inject]
	private UiStateStore UiState { get; set; } = default!;

	[Inject]
	private ISettingsService Settings { get; set; } = default!;

	[Inject]
	private ITransferQueue Transfers { get; set; } = default!;

	[Inject]
	private IJSRuntime JSRuntime { get; set; } = default!;

	// Optional: only a host whose window has no title bar of its own registers IWindowChrome.
	[Inject]
	private IServiceProvider Services { get; set; } = default!;

	public async ValueTask DisposeAsync()
	{
		_disposed = true;
		Shell.Changed -= OnShellChanged;
		Settings.Changed -= OnSettingsChanged;
		Transfers.Changed -= OnTransfersChanged;
		Commands.Unregister();
		await InputBridge.DetachAsync();
		await DetachChromeAsync();
	}

	protected override void OnInitialized()
	{
		_showStatusBar = Settings.Get<AppearanceSettings>().ShowStatusBar;
		_lastActiveTransfers = Transfers.ActiveCount;
		Shell.Changed += OnShellChanged;
		Settings.Changed += OnSettingsChanged;
		Transfers.Changed += OnTransfersChanged;
		Commands.Register();

		_chrome = Services.GetService<IWindowChrome>();
		if (_chrome is not null)
		{
			_chromeStyle = ChromeStyle(_chrome.Insets);
			_chrome.InsetsChanged += OnChromeInsetsChanged;
		}
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (firstRender)
		{
			await InputBridge.AttachAsync();
			await AttachChromeAsync();
		}
	}

	private static string Px(double value) => string.Create(CultureInfo.InvariantCulture, $"{Math.Round(value)}px");

	// Rounded up: a bar that stops a fraction of a pixel short of the window's buttons would slide under them.
	private static string ChromeStyle(WindowChromeInsets insets) => string.Create(
		CultureInfo.InvariantCulture,
		$"--mt-chrome-left:{Math.Ceiling(insets.Left)}px;--mt-chrome-right:{Math.Ceiling(insets.Right)}px;--mt-chrome-height:{Math.Ceiling(insets.Height)}px");

	private async Task AttachChromeAsync()
	{
		if (_chrome is null || _disposed)
		{
			return;
		}

		_chromeModule = new JsModule(JSRuntime, WindowChromeModulePath);
		_dragRegions = new DragRegionReceiver(_chrome);
		_dragRegionsReference = DotNetObjectReference.Create(_dragRegions);
		try
		{
			_chromeTracker = await _chromeModule.InvokeAsync<IJSObjectReference>("attach", _topBar, _dragRegionsReference);
		}
		catch (Exception ex) when (JsModule.IsTeardown(ex))
		{
			// The window closed, or the vault locked, while the script was attaching.
			return;
		}

		// The vault locked while the script was attaching, so the detach found no tracker to stop.
		if (_disposed)
		{
			await DetachChromeAsync();
		}
	}

	private async Task DetachChromeAsync()
	{
		if (_chrome is null)
		{
			return;
		}

		_chrome.InsetsChanged -= OnChromeInsetsChanged;

		// A report still on its way from the page must not land after the reset below.
		_dragRegions?.Stop();

		// The lock screen has no top bar; the window gets its own title bar strip back so it can still be moved.
		_chrome.ResetDragRegions();

		IJSObjectReference? tracker = _chromeTracker;
		DotNetObjectReference<DragRegionReceiver>? reference = _dragRegionsReference;
		JsModule? module = _chromeModule;
		_chromeTracker = null;
		_dragRegionsReference = null;
		_chromeModule = null;

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

	private void OnConnectionsPanelResized(double width) =>
		UiState.Update(state => state with { ConnectionsPanelWidth = width });

	private void OnTransfersPanelResized(double height) =>
		UiState.Update(state => state with { TransfersPanelHeight = height });

	private void OnShellChanged() => _ = InvokeAsync(StateHasChanged);

	private void OnChromeInsetsChanged() => _ = InvokeAsync(() =>
	{
		if (_chrome is not null && !_disposed)
		{
			_chromeStyle = ChromeStyle(_chrome.Insets);
			StateHasChanged();
		}
	});

	private void OnSettingsChanged(string sectionKey)
	{
		if (sectionKey == AppearanceSettings.SectionKey)
		{
			_ = InvokeAsync(() =>
			{
				_showStatusBar = Settings.Get<AppearanceSettings>().ShowStatusBar;
				StateHasChanged();
			});
		}
	}

	private void OnTransfersChanged()
	{
		int active = Transfers.ActiveCount;
		int previous = Interlocked.Exchange(ref _lastActiveTransfers, active);
		if (previous == 0 && active > 0)
		{
			_ = InvokeAsync(() => Shell.SetTransfersPanelCollapsed(false));
		}
	}

	/// <summary>Receives the top bar's empty stretches from <c>window-chrome.js</c> and hands them to the window.</summary>
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
