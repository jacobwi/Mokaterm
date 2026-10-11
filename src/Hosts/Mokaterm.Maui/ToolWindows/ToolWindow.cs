using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.DependencyInjection;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Workspace;

namespace Mokaterm.Maui.ToolWindows;

/// <summary>
/// A session tool in a native window of its own: one BlazorWebView whose page is <see cref="ToolWindowView"/>, running
/// on services borrowed from the window that opened it. It keeps the system's title bar rather than the main window's
/// custom one: that one is the shell's top bar reporting its empty stretches as drag regions, and a tool's page has no
/// such bar, while the system's shows the title "Docker · user@host" that the taskbar and Alt+Tab list it by.
/// </summary>
internal sealed class ToolWindow : Window
{
	private const double WideWidth = 1180;
	private const double WideHeight = 800;
	private const double StandardWidth = 760;
	private const double StandardHeight = 560;

	/// <param name="bounds">Where to open, or null for the tool's default size; a position that is not a number is left to the system.</param>
	public ToolWindow(ToolWindowRequest request, IServiceProvider borrowed, ToolWindowBounds? bounds)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(borrowed);
		Request = request;

		// The window holds the borrowed container for as long as it lives; nothing else does.
		Borrowed = borrowed;
		Title = request.Title;
		MinimumWidth = ToolWindowBounds.MinWidth;
		MinimumHeight = ToolWindowBounds.MinHeight;
		if (bounds is not null)
		{
			Width = bounds.Width;
			Height = bounds.Height;
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
			Parameters = new Dictionary<string, object?>
			{
				[nameof(ToolWindowView.SessionId)] = request.SessionId,
				[nameof(ToolWindowView.ToolKey)] = request.ToolKey,
			},
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

	/// <summary>Where the window is now, in the units the next one is opened with.</summary>
	public ToolWindowBounds CurrentBounds => new(X, Y, Width, Height);

	public bool Shows(Guid sessionId, string? toolKey) =>
		Request.SessionId == sessionId && (toolKey is null || string.Equals(Request.ToolKey, toolKey, StringComparison.Ordinal));

	private static void OnUrlLoading(object? sender, UrlLoadingEventArgs e) => UrlLoadingPolicy.Apply(e);

	private static void OnWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
	{
#if WINDOWS
		Platforms.Windows.WebView2Setup.Configure(e.WebView);
#endif
	}
}
