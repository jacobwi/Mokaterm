using WebView2 = Microsoft.UI.Xaml.Controls.WebView2;
using WinPoint = Windows.Foundation.Point;

namespace Mokaterm.Maui.Platforms.Windows;

/// <summary>
/// Where the page sits inside the window. One place for it, because the title bar turns page rectangles into window
/// pixels while the window's drop target turns window pixels back into page coordinates: the same offset read from two
/// directions, and a second copy of it would eventually move one of them.
/// </summary>
internal static class WebViewLayout
{
	/// <summary>
	/// The WebView's top left corner in view pixels, which are the page's own pixels while page zoom is off. That is
	/// the window's own top left corner while MAUI's title bar row is hidden, as it is here.
	/// </summary>
	public static WinPoint Origin(WebView2? webView)
	{
		if (webView is null)
		{
			return new WinPoint(0, 0);
		}

		try
		{
			return webView.TransformToVisual(null).TransformPoint(new WinPoint(0, 0));
		}
		catch (ArgumentException)
		{
			// Not in the window's tree (yet); the page fills the window then.
			return new WinPoint(0, 0);
		}
	}
}
