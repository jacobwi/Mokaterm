using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Platform;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Moka.Red.Core.Theming;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Platform;
using Windows.Graphics;
using Windows.UI.ViewManagement;
using ColorHelper = Microsoft.UI.ColorHelper;
using MauiWindow = Microsoft.Maui.Controls.Window;
using WebView2 = Microsoft.UI.Xaml.Controls.WebView2;
using WinColor = Windows.UI.Color;
using WinPoint = Windows.Foundation.Point;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace Mokaterm.Maui.Platforms.Windows;

/// <summary>
/// Makes the page's top bar the window's title bar. MAUI's own title bar row is hidden, so the page reaches the top of
/// the window, and the window keeps its content extended into the title bar: the system still draws minimize, maximize
/// and close over the page, in Moka colours, with snap layouts on maximize. The empty stretches the page reports become
/// the window's caption area, which is what lets dragging, double click to maximize and the window menu work over a
/// WebView2 that would otherwise take every click.
/// </summary>
internal sealed class WindowChrome : IWindowChrome
{
	private const int DwmUseImmersiveDarkMode = 20;
	private const int DwmCaptionColor = 35;

	private readonly ISettingsService _settings;

	// The event only fires while this object is alive.
	private readonly UISettings _systemColors = new();

	private WinUIWindow? _window;
	private nint _hwnd;

	// Kept apart from the window: the queue may be used from any thread, the window only from its own.
	private DispatcherQueue? _queue;
	private AppWindow? _appWindow;
	private InputNonClientPointerSource? _pointerSource;
	private WebView2? _webView;
	private XamlRoot? _xamlRoot;

	// Null while the page shows no top bar; empty while it has one with nothing to drag.
	private WindowDragRegion[]? _pageRegions;
	private CaptionRect[] _caption = [];
	private WindowChromeInsets _insets;

	public WindowChrome(ISettingsService settings) => _settings = settings;

	public event Action? InsetsChanged;

	public WindowChromeInsets Insets => _insets;

	public void SetDragRegions(IReadOnlyList<WindowDragRegion> regions)
	{
		WindowDragRegion[] copy = [.. regions];
		RunOnWindowThread(() =>
		{
			_pageRegions = copy;
			ApplyCaption(force: false);
		});
	}

	public void ResetDragRegions() => RunOnWindowThread(() =>
	{
		_pageRegions = null;
		ApplyCaption(force: false);
	});

	/// <summary>Takes over the title bar of a window MAUI just created. Its handler is connected at this point.</summary>
	public void Attach(WinUIWindow native)
	{
		if (native.GetWindow() is not MauiWindow window || ReferenceEquals(native, _window))
		{
			return;
		}

		// A hidden MAUI title bar gives its row to the page. IsVisible only reaches the window through the title bar's
		// handler, which setting TitleBar on a connected window creates, so the order of these two lines matters.
		TitleBar titleBar = new();
		window.TitleBar = titleBar;
		titleBar.IsVisible = false;

		Connect(native);

		BlazorWebView? view = window.Page?.GetVisualTreeDescendants().OfType<BlazorWebView>().FirstOrDefault();
		if (view is not null)
		{
			view.BlazorWebViewInitialized += (_, e) => AttachWebView(e.WebView);
		}
	}

	private static WinColor? FromHex(string value)
	{
		// The palette entries used here are #rrggbb; anything else leaves the system colour in place.
		if (value.Length == 7 && value[0] == '#'
			&& uint.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint rgb))
		{
			return ColorHelper.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
		}

		return null;
	}

	private static CaptionRect[] FromWindow(RectInt32[] rects) =>
		[.. rects.Select(rect => new CaptionRect(rect.X, rect.Y, rect.Width, rect.Height))];

	private static RectInt32[] ToWindow(CaptionRect[] rects) =>
		[.. rects.Select(rect => new RectInt32(rect.X, rect.Y, rect.Width, rect.Height))];

	private void Connect(WinUIWindow window)
	{
		_window = window;
		_hwnd = window.GetWindowHandle();
		_queue = window.DispatcherQueue;
		_appWindow = window.AppWindow;
		_pointerSource = InputNonClientPointerSource.GetForWindowId(_appWindow.Id);

		// MAUI extends the content already; this keeps the page under the system's buttons if that default changes.
		// Tall buttons (48) are what Windows asks for when the title bar holds controls, and they match Moka's dense bar;
		// at the standard 32 their glyphs would sit off centre from the bar's icons. wwwroot/index.html gives the page's
		// top bar the same height (--mt-top-bar-height), which is also where toasts start.
		_appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
		_appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

		_appWindow.Changed += OnAppWindowChanged;
		_pointerSource.RegionsChanged += OnRegionsChanged;
		window.Activated += OnWindowActivated;
		window.Closed += OnWindowClosed;
		_systemColors.ColorValuesChanged += OnSystemColorsChanged;
		_settings.Changed += OnSettingsChanged;

		ApplyColors();
		UpdateInsets();
		ApplyCaption(force: true);
		_ = ApplyColorsOnceSettingsLoadAsync();
	}

	private void AttachWebView(WebView2 webView)
	{
		_webView = webView;
		webView.Loaded += (_, _) => OnLayoutChanged();
		OnLayoutChanged();
	}

	private void OnLayoutChanged()
	{
		if (_webView?.XamlRoot is { } root && !ReferenceEquals(root, _xamlRoot))
		{
			if (_xamlRoot is not null)
			{
				_xamlRoot.Changed -= OnXamlRootChanged;
			}

			_xamlRoot = root;
			root.Changed += OnXamlRootChanged;
		}

		UpdateInsets();
		ApplyCaption(force: false);
	}

	// Page pixels are the WebView's pixels: page zoom is off, so its scale is the window's.
	private double Scale => _xamlRoot?.RasterizationScale ?? _window?.Content?.XamlRoot?.RasterizationScale ?? 1.0;

	private void UpdateInsets()
	{
		if (_appWindow is null)
		{
			return;
		}

		AppWindowTitleBar bar = _appWindow.TitleBar;
		WindowChromeInsets insets = CaptionRegions.Insets(bar.LeftInset, bar.RightInset, bar.Height, Scale);
		if (insets != _insets)
		{
			_insets = insets;
			InsetsChanged?.Invoke();
		}
	}

	private void ApplyCaption(bool force)
	{
		if (_appWindow is null || _pointerSource is null)
		{
			return;
		}

		AppWindowTitleBar bar = _appWindow.TitleBar;
		int clientWidth = _appWindow.ClientSize.Width;
		CaptionRect[] caption;
		if (_pageRegions is null)
		{
			caption = CaptionRegions.Default(clientWidth, bar.LeftInset, bar.RightInset, bar.Height);
		}
		else
		{
			WinPoint origin = WebViewLayout.Origin(_webView);
			caption = CaptionRegions.FromPage(_pageRegions, Scale, origin.X, origin.Y, clientWidth, bar.LeftInset, bar.RightInset);
		}

		if (!force && caption.AsSpan().SequenceEqual(_caption))
		{
			return;
		}

		_caption = caption;
		if (caption.Length == 0)
		{
			_pointerSource.ClearRegionRects(NonClientRegionKind.Caption);
		}
		else
		{
			_pointerSource.SetRegionRects(NonClientRegionKind.Caption, ToWindow(caption));
		}
	}

	private void ApplyColors()
	{
		if (_appWindow is null)
		{
			return;
		}

		bool light = _settings.Get<AppearanceSettings>().Mode == ThemeMode.Light;
		MokaPalette palette = light ? MokaPalette.Light : MokaPalette.Dark;
		AppWindowTitleBar bar = _appWindow.TitleBar;

		// DWM still draws what is left of the frame: the border, and a sliver of caption above the page when a maximized
		// window has moved to a screen with another scale. In the app's mode and the bar's colour, that sliver blends in
		// instead of showing as a light line. Both attributes fail quietly on Windows versions without them.
		int darkMode = light ? 0 : 1;
		_ = DwmSetWindowAttribute(_hwnd, DwmUseImmersiveDarkMode, ref darkMode, sizeof(int));
		if (FromHex(palette.Surface) is { } surface)
		{
			int captionColor = surface.R | (surface.G << 8) | (surface.B << 16);
			_ = DwmSetWindowAttribute(_hwnd, DwmCaptionColor, ref captionColor, sizeof(int));
		}

		// Transparent lets the page's bar show behind the buttons.
		bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
		bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
		bar.ButtonForegroundColor = FromHex(palette.OnSurface);
		bar.ButtonHoverForegroundColor = FromHex(palette.OnSurface);
		bar.ButtonHoverBackgroundColor = FromHex(palette.SurfaceHover);
		bar.ButtonPressedForegroundColor = FromHex(palette.OnSurface);
		bar.ButtonPressedBackgroundColor = FromHex(palette.Surface3);
		bar.ButtonInactiveForegroundColor = FromHex(palette.OnSurfaceTertiary);
	}

	// The page loads the settings too; until they are read the default theme's colours stay.
	private async Task ApplyColorsOnceSettingsLoadAsync()
	{
		try
		{
			await _settings.LoadAsync();
		}
		catch (Exception)
		{
			// The page reports a settings file it cannot read; the default colours are right for its fallback.
			return;
		}

		RunOnWindowThread(ApplyColors);
	}

	private void RunOnWindowThread(Action action)
	{
		DispatcherQueue? queue = _queue;
		if (queue is null || queue.HasThreadAccess)
		{
			action();
		}
		else
		{
			_ = queue.TryEnqueue(() => action());
		}
	}

	private void OnSettingsChanged(string sectionKey)
	{
		if (sectionKey == AppearanceSettings.SectionKey)
		{
			RunOnWindowThread(ApplyColors);
		}
	}

	// MAUI puts its own caption colours back when the system colours change (on .NET 9 the foreground too) and queues that
	// on the window's thread. Queued as well, this runs after it.
	private void OnSystemColorsChanged(UISettings sender, object args) => _ = _queue?.TryEnqueue(ApplyColors);

	private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
	{
		if (args.DidSizeChange || args.DidPresenterChange)
		{
			UpdateInsets();
			ApplyCaption(force: false);
		}
	}

	private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
	{
		UpdateInsets();
		ApplyCaption(force: false);
	}

	// The window's button widths are known once it is on screen.
	private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
	{
		UpdateInsets();
		ApplyCaption(force: false);
	}

	// While its template loads, and whenever its title bar row changes size, MAUI sets the window's drag rectangles from
	// its hidden title bar: a strip of zero height. They land on the caption regions this class owns, so whatever it
	// leaves is replaced. With no regions of ours there is nothing to restore, and the system then reports a default
	// strip that it does not actually use.
	private void OnRegionsChanged(InputNonClientPointerSource sender, NonClientRegionsChangedEventArgs args)
	{
		if (_caption.Length > 0
			&& args.ChangedRegions.Contains(NonClientRegionKind.Caption)
			&& !FromWindow(sender.GetRegionRects(NonClientRegionKind.Caption)).AsSpan().SequenceEqual(_caption))
		{
			_ = _queue?.TryEnqueue(() => ApplyCaption(force: true));
		}
	}

	private void OnWindowClosed(object sender, WindowEventArgs args)
	{
		_settings.Changed -= OnSettingsChanged;
		_systemColors.ColorValuesChanged -= OnSystemColorsChanged;
		if (_appWindow is not null)
		{
			_appWindow.Changed -= OnAppWindowChanged;
		}

		if (_pointerSource is not null)
		{
			_pointerSource.RegionsChanged -= OnRegionsChanged;
		}

		if (_xamlRoot is not null)
		{
			_xamlRoot.Changed -= OnXamlRootChanged;
		}

		if (_window is not null)
		{
			_window.Activated -= OnWindowActivated;
			_window.Closed -= OnWindowClosed;
		}

		_window = null;
		_hwnd = 0;
		_queue = null;
		_appWindow = null;
		_pointerSource = null;
		_webView = null;
		_xamlRoot = null;
	}

	[DllImport("dwmapi.dll", ExactSpelling = true)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
