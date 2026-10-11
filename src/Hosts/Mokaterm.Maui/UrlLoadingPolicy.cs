using Microsoft.AspNetCore.Components.WebView;

namespace Mokaterm.Maui;

/// <summary>Where a link clicked in any of the app's pages may go: the main window and every tool window ask here.</summary>
internal static class UrlLoadingPolicy
{
	public static void Apply(UrlLoadingEventArgs e)
	{
		ArgumentNullException.ThrowIfNull(e);

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
