using Microsoft.Web.WebView2.Core;
using Mokaterm.Maui.Services;

namespace Mokaterm.Maui.Platforms.Windows;

internal static class WebView2Setup
{
	private const string VariablePrefix = "WEBVIEW2_";

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

	public static void Configure(Microsoft.UI.Xaml.Controls.WebView2 webView, DesktopDropCoordinator dropCoordinator)
	{
		// Matches the Moka background so resizing and startup never flash white.
		webView.DefaultBackgroundColor = Microsoft.UI.ColorHelper.FromArgb(255, 6, 6, 8);

		CoreWebView2 core = webView.CoreWebView2;

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

		core.WebMessageReceived += (_, args) =>
		{
			string? message;
			try
			{
				message = args.TryGetWebMessageAsString();
			}
			catch (ArgumentException)
			{
				// Blazor's own IPC also uses this channel; non-string messages are not ours.
				return;
			}

			if (message is null || !message.StartsWith(DesktopDropCoordinator.MessagePrefix, StringComparison.Ordinal) || args.AdditionalObjects is null)
			{
				return;
			}

			List<string> paths = [];
			foreach (object item in args.AdditionalObjects)
			{
				if (item is CoreWebView2File file && !string.IsNullOrEmpty(file.Path))
				{
					paths.Add(file.Path);
				}
			}

			dropCoordinator.Complete(message[DesktopDropCoordinator.MessagePrefix.Length..], paths);
		};
	}
}
