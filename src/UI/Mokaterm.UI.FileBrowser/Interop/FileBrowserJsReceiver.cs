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

	/// <summary>A keyboard shortcut. <paramref name="pageRows"/> is how many rows fit in the list, for Page Up and Page Down.</summary>
	[JSInvokable]
	public Task OnKeyCommand(string command, bool shift, int pageRows) => _browser.HandleKeyCommandAsync(command, shift, pageRows);

	/// <summary>Letters typed while the list has focus, to jump to a name.</summary>
	[JSInvokable]
	public Task OnTypeAhead(string prefix) => _browser.HandleTypeAheadAsync(prefix);

	/// <summary>The browser's width crossed a step passed to <c>attachBrowser</c>; <paramref name="step"/> counts the steps it reaches.</summary>
	[JSInvokable]
	public Task OnWidthChanged(int step) => _browser.HandleWidthChangedAsync(step);

	/// <summary>An entry was dragged onto a folder row.</summary>
	[JSInvokable]
	public Task OnEntryDrop(string sourcePath, string targetDirectory) => _browser.HandleEntryDropAsync(sourcePath, targetDirectory);
}
