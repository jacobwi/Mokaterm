using Microsoft.AspNetCore.Components.WebView;
using Mokaterm.Maui.Services;

namespace Mokaterm.Maui;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
		StartupTrace.Mark("Main page created");
	}

	private void OnBlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
	{
#if WINDOWS
		Platforms.Windows.WebView2Setup.Configure(e.WebView);
#endif
	}

	private void OnUrlLoading(object? sender, UrlLoadingEventArgs e) => UrlLoadingPolicy.Apply(e);
}
