using Microsoft.AspNetCore.Components.WebView;
using Mokaterm.Maui.Services;

namespace Mokaterm.Maui;

public partial class MainPage : ContentPage
{
	private readonly DesktopAppearance _appearance;

	public MainPage(DesktopAppearance appearance)
	{
		_appearance = appearance;
		InitializeComponent();
		ShowBackground();
		appearance.Changed += OnAppearanceChanged;
		StartupTrace.Mark("Main page created");
	}

	// The page lives as long as the app, so nothing takes the handler off again.
	private void OnAppearanceChanged() => Dispatcher.Dispatch(ShowBackground);

	private void ShowBackground() => BackgroundColor = Color.FromArgb(_appearance.Background);

	private void OnBlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
	{
#if WINDOWS
		Platforms.Windows.WebView2Setup.Configure(e.WebView, _appearance);
#endif
	}

	private void OnUrlLoading(object? sender, UrlLoadingEventArgs e) => UrlLoadingPolicy.Apply(e);
}
