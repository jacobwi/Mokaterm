// OS file drops for the desktop host. WinUI refuses an external drop before a single drag event reaches WebView2
// content (microsoft-ui-xaml#10142), so dragenter, dragover and drop never fire here and the page cannot see the drag
// at all. The window carries a drop target of its own instead (Platforms/Windows/NativeFileDrop), which drives the
// entry points at the bottom of this file: it says where the pointer is so the overlay can follow it, and on a drop it
// names the point to find the zone under. The dropped paths are already waiting in .NET under the drop id by then, so
// the zone only has to hand its id back.
import { attachDropZone } from '../_content/Mokaterm.UI.Common/js/dropzone.js';

// Every attached zone, by element. The host hit tests against this, so a zone that is disposed has to leave it.
const zones = new Map();

// The zone a point is inside, from the element on top of it: an overlay, a table cell or a row is a child of its zone.
function zoneAt(x, y) {
	for (let element = document.elementFromPoint(x, y); element; element = element.parentElement) {
		const zone = zones.get(element);
		if (zone) {
			return zone;
		}
	}

	return null;
}

// One zone at a time, so a drag that crosses from one file panel into another leaves the first one behind.
function hoverOnly(target) {
	for (const zone of zones.values()) {
		zone.handle.hover(zone === target);
	}
}

export function attach(element, dotNetRef) {
	// A circuit or window that is going away has nobody left to tell.
	const report = (method, ...args) => dotNetRef.invokeMethodAsync(method, ...args).catch(() => { });

	// No drop callback: the host delivers the drop. The listeners still go on, for their preventDefault.
	const handle = attachDropZone(element, over => report('OnDragOver', over));
	const zone = { handle, report };
	zones.set(element, zone);

	return {
		dispose() {
			zones.delete(element);
			handle.dispose();
		},
	};
}

// What the host calls. ExecuteScript runs in the page's global scope, so these three have to be reachable from there
// while the rest of the module stays private. The coordinates are CSS pixels, measured from the top left of the page.
window.mokatermNativeDrop = {
	over(x, y) {
		hoverOnly(zoneAt(x, y));
	},

	leave() {
		hoverOnly(null);
	},

	drop(x, y, dropId, ctrlKey, shiftKey, altKey) {
		const zone = zoneAt(x, y);
		hoverOnly(null);
		zone?.report('OnDrop', dropId, ctrlKey, shiftKey, altKey);
	},
};
