using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Mokaterm.Maui.Services;

namespace Mokaterm.Maui.Platforms.Windows;

internal static class WebView2Setup
{
	private const string VariablePrefix = "WEBVIEW2_";
	private const string RuntimeDownload = "https://developer.microsoft.com/microsoft-edge/webview2/";
	private const int FileNotFound = unchecked((int)0x80070002);

	/// <summary>Runs before the first WebView2 is created, because the loader reads these variables at that point.</summary>
	public static void PrepareEnvironment()
	{
#if !DEBUG
		// The loader takes browser arguments, a browser folder and a script debugger pipe from the environment, which is
		// how --remote-debugging-port gets into an app without its code agreeing. Release builds take none of them.
		foreach (string name in Environment.GetEnvironmentVariables().Keys.OfType<string>())
		{
			if (name.StartsWith(VariablePrefix, StringComparison.OrdinalIgnoreCase))
			{
				Environment.SetEnvironmentVariable(name, null);
			}
		}
#endif

		// BlazorWebView on WinUI ignores BlazorWebViewInitializingEventArgs.UserDataFolder: it creates an environment
		// with it and then initializes the control without that environment. The profile would land next to the
		// executable, which is not writable once installed under Program Files. The loader honours this variable.
		Environment.SetEnvironmentVariable(VariablePrefix + "USER_DATA_FOLDER", DesktopPaths.WebViewDirectory);
	}

	public static bool RuntimeMissing()
	{
		try
		{
			return string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
		}
		catch (Exception ex) when (ex.HResult == FileNotFound)
		{
			return true;
		}
		catch (Exception ex)
		{
			// Only a runtime known to be missing stops the start. Anything else is left to WinUI, which meets the same
			// runtime a moment later, and kept for the crash log in case it does fail there.
			CrashLog.Write("CoreWebView2Environment.GetAvailableBrowserVersionString", ex, terminating: false);
			return false;
		}
	}

	public static void OfferRuntimeDownload()
	{
		bool open = NativeMessageBox.AskError(
			"Mokaterm needs the Microsoft Edge WebView2 Runtime, and this PC does not have it. Microsoft offers it as a " +
			"free download (the Evergreen Bootstrapper). Install it, then start Mokaterm again.\n\n" +
			"Open the download page now?");
		if (open)
		{
			try
			{
				using Process? _ = Process.Start(new ProcessStartInfo(RuntimeDownload) { UseShellExecute = true });
			}
			catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
			{
				NativeMessageBox.ShowError($"No browser could open the page. The address is {RuntimeDownload}");
			}
		}
	}

	public static void Configure(Microsoft.UI.Xaml.Controls.WebView2 webView, DesktopAppearance appearance)
	{
		CoreWebView2 core = webView.CoreWebView2;
		StartupTrace.Mark("WebView2 ready");
		FollowAppearance(webView, appearance);
		core.DOMContentLoaded += (_, _) => StartupTrace.Mark("Page loaded");

		// The terminal zooms itself with Ctrl+wheel and Ctrl+=; page zoom would scale the whole window instead.
		core.Settings.IsZoomControlEnabled = false;

#if !DEBUG
		// F5 or Ctrl+R would reload the page and close every open session with it, and Ctrl+F or Ctrl+P open browser UI
		// that has no place in the app. Debug builds keep them, DevTools shortcuts included.
		core.Settings.AreBrowserAcceleratorKeysEnabled = false;
#endif

		// The terminal's context menu pastes through navigator.clipboard.readText, which WebView2 would otherwise deny.
		core.PermissionRequested += (_, args) =>
		{
			if (args.PermissionKind == CoreWebView2PermissionKind.ClipboardRead)
			{
				args.State = CoreWebView2PermissionState.Allow;
			}
		};
	}

	// The page's background, so resizing and startup never flash another colour, and the app's mode as the page's
	// prefers-color-scheme, which is what the boot and closing screens in index.html follow. This runs before the first
	// navigation, so the boot screen starts in it. The scheme belongs to the profile every window shares.
	private static void FollowAppearance(Microsoft.UI.Xaml.Controls.WebView2 webView, DesktopAppearance appearance)
	{
		void Apply()
		{
			if (WindowColors.FromHex(appearance.Background) is { } background)
			{
				webView.DefaultBackgroundColor = background;
			}

			if (webView.CoreWebView2 is { } core)
			{
				core.Profile.PreferredColorScheme = appearance.IsLight ? CoreWebView2PreferredColorScheme.Light : CoreWebView2PreferredColorScheme.Dark;
			}
		}

		void OnChanged() => _ = webView.DispatcherQueue.TryEnqueue(Apply);

		Apply();
		appearance.Changed += OnChanged;
		webView.Unloaded += (_, _) => appearance.Changed -= OnChanged;
	}
}
