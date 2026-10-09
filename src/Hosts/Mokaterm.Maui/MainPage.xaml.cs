using Microsoft.AspNetCore.Components.WebView;

namespace Mokaterm.Maui;

public partial class MainPage : ContentPage
{
	public MainPage() => InitializeComponent();

	private void OnBlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
	{
#if WINDOWS
		Platforms.Windows.WebView2Setup.Configure(e.WebView);
#endif
	}

	private void OnUrlLoading(object? sender, UrlLoadingEventArgs e)
	{
		// BlazorWebView hands every URL outside the app origin to the system's handler for its scheme, which covers
		// file:, ms-settings: and whatever protocol an installed app registered. Only web links leave the app; a file
		// dropped outside a drop zone, or a link with any other scheme, goes nowhere.
		if (e.UrlLoadingStrategy == UrlLoadingStrategy.OpenExternally && !IsWebLink(e.Url))
		{
			e.UrlLoadingStrategy = UrlLoadingStrategy.CancelLoad;
		}
	}

	// Uri.Scheme throws for a relative URI, hence the explicit order.
	private static bool IsWebLink(Uri url) =>
		url.IsAbsoluteUri && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp);
}
