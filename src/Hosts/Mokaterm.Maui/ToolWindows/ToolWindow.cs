using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.DependencyInjection;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Tools;

namespace Mokaterm.Maui.ToolWindows;

/// <summary>
/// A tool in a native window of its own: one BlazorWebView whose page is <see cref="ToolWindowView"/>, running on
/// services borrowed from the window that opened it. Like the main window it has no system title bar: the tool's header
/// is the title bar, through a chrome of its own (<see cref="Chrome"/>), with the window's buttons drawn over its end.
/// The title "Docker · user@host" stays the window's, for the taskbar and Alt+Tab.
/// </summary>
internal sealed class ToolWindow : Window
{
	private const double WideWidth = 1180;
	private const double WideHeight = 800;
	private const double StandardWidth = 760;
	private const double StandardHeight = 560;

	/// <param name="request">What to show, and where its place is remembered.</param>
	/// <param name="borrowed">The window's services (<see cref="ToolPageServices.Borrow"/>), which it holds for its whole life.</param>
	/// <param name="bounds">Where to open, or null for the tool's default size; a position that is not a number is left to the system.</param>
	/// <param name="chrome">The chrome bound to this window, which its page also finds as its <see cref="IWindowChrome"/>.</param>
	public ToolWindow(ToolWindowRequest request, IServiceProvider borrowed, ToolWindowBounds? bounds, IWindowChrome? chrome)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(borrowed);
		Request = request;
		Borrowed = borrowed;
		Chrome = chrome;
		Title = request.Title;
		MinimumWidth = ToolWindowBounds.MinWidth;
		MinimumHeight = ToolWindowBounds.MinHeight;
		if (bounds is not null)
		{
			Width = bounds.Width;
			Height = bounds.Height;
			KeepOnTop = bounds.KeepOnTop;
			if (double.IsFinite(bounds.X) && double.IsFinite(bounds.Y))
			{
				X = bounds.X;
				Y = bounds.Y;
			}
		}
		else
		{
			bool wide = request.Size == SessionToolSize.Wide;
			Width = wide ? WideWidth : StandardWidth;
			Height = wide ? WideHeight : StandardHeight;
		}

		ToolWebView view = new(borrowed.GetRequiredService<IServiceScopeFactory>())
		{
			HostPage = "wwwroot/index.html",
		};
		view.RootComponents.Add(new RootComponent { Selector = "head::after", ComponentType = typeof(HeadOutlet) });
		view.RootComponents.Add(new RootComponent
		{
			Selector = "#app",
			ComponentType = typeof(ToolWindowView),
			Parameters = new Dictionary<string, object?> { [nameof(ToolWindowView.Target)] = request.Target },
		});
		view.BlazorWebViewInitialized += OnWebViewInitialized;
		view.UrlLoading += OnUrlLoading;

		Page = new ContentPage
		{
			BackgroundColor = Color.FromArgb("#060608"),
			Content = view,
		};
	}

	public ToolWindowRequest Request { get; }

	public IServiceProvider Borrowed { get; }

	/// <summary>The title bar this window's page draws, or null on a platform without one.</summary>
	public IWindowChrome? Chrome { get; }

	/// <summary>Whether the window stays above other windows. The host applies it to the native window.</summary>
	public bool KeepOnTop { get; set; }

	/// <summary>
	/// Set before the app closes the window itself, so a close without it is the user's own: the window's close button.
	/// </summary>
	public bool ClosedByApp { get; set; }

	/// <summary>Where the window is now and whether it stays on top, in the units the next one is opened with.</summary>
	public ToolWindowBounds CurrentBounds => new(X, Y, Width, Height) { KeepOnTop = KeepOnTop };

	public bool Shows(ToolWindowTarget target) => Request.Target == target;

	private static void OnUrlLoading(object? sender, UrlLoadingEventArgs e) => UrlLoadingPolicy.Apply(e);

	private static void OnWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
	{
#if WINDOWS
		Platforms.Windows.WebView2Setup.Configure(e.WebView);
#endif
	}
}
