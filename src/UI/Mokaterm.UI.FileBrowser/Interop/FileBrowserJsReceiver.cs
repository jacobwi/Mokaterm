using Microsoft.JSInterop;

namespace Mokaterm.UI.FileBrowser.Interop;

/// <summary>
/// Receives calls from <c>filebrowser.js</c>. JS-invokable methods must be public, so they live here instead of on the
/// browser component, whose public surface other projects compile against.
/// </summary>
internal sealed class FileBrowserJsReceiver
{
	private readonly RemoteFileBrowser _browser;

	public FileBrowserJsReceiver(RemoteFileBrowser browser) => _browser = browser;

	/// <summary>A keyboard shortcut. <paramref name="cursor"/> is the path of the row under the keyboard cursor.</summary>
	[JSInvokable]
	public Task OnKeyCommand(string command, string? cursor) => _browser.HandleKeyCommandAsync(command, cursor);

	/// <summary>Letters typed while the list has focus, to jump to a name.</summary>
	[JSInvokable]
	public Task OnTypeAhead(string prefix, string? cursor) => _browser.HandleTypeAheadAsync(prefix, cursor);

	/// <summary>The browser's width crossed a step passed to <c>attachBrowser</c>; <paramref name="step"/> counts the steps it reaches.</summary>
	[JSInvokable]
	public Task OnWidthChanged(int step) => _browser.HandleWidthChangedAsync(step);
}
