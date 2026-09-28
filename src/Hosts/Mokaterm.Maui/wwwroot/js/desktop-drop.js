// OS file drops for the desktop host. WebView2 turns the dropped File objects into real paths on the .NET side when
// they travel through postMessageWithAdditionalObjects; Blazor interop alone could only stream their bytes. The hover
// bookkeeping is shared with the browser bridge, so both behave the same when a drag leaves the window.
import { attachDropZone, newDropId } from '../_content/Mokaterm.UI.Common/js/dropzone.js';

export function attach(element, dotNetRef, messagePrefix) {
	// A circuit or window that is going away has nobody left to tell.
	const report = (method, ...args) => dotNetRef.invokeMethodAsync(method, ...args).catch(() => { });

	return attachDropZone(
		element,
		over => report('OnDragOver', over),
		event => {
			const files = event.dataTransfer.files;
			if (!files || files.length === 0 || !window.chrome?.webview?.postMessageWithAdditionalObjects) {
				return;
			}

			const dropId = newDropId();
			window.chrome.webview.postMessageWithAdditionalObjects(messagePrefix + dropId, files);
			report('OnDrop', dropId, event.ctrlKey, event.shiftKey, event.altKey);
		});
}
