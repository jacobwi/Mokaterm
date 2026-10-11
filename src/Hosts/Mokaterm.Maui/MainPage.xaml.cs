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

	private void OnUrlLoading(object? sender, UrlLoadingEventArgs e) => UrlLoadingPolicy.Apply(e);
}
