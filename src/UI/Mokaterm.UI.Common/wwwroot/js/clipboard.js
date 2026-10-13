// Clears a copied secret from the clipboard. Browsers and WebView2 refuse clipboard writes while the page is not
// focused, which is exactly when a copied password is being pasted into another window, so a page in the background
// clears as soon as it is focused again instead of leaving the secret behind.
//
// The deadline lives in .NET (ClipboardClearSchedule) and is shared by a window and its tool windows, so the clear for
// one copy can reach this page after a newer copy was made in another window. Every copy is numbered: a clear for a
// copy older than the newest this page has heard of does nothing, whether it is still waiting for focus or arrives late.

let waiting = null;
let newestCopy = 0;

function stopWaiting() {
	if (waiting) {
		window.removeEventListener('focus', waiting.listener);
		waiting = null;
	}
}

function waitForFocus(copy) {
	// A refused write gets here after a promise, by which time a newer copy may have been made.
	if (copy < newestCopy) {
		return;
	}

	stopWaiting();
	const listener = () => {
		waiting = null;
		navigator.clipboard.writeText('').catch(() => {
			// Still refused (no permission at all); nothing more a page can do.
		});
	};
	waiting = { copy, listener };
	window.addEventListener('focus', listener, { once: true });
}

export function clearClipboard(copy) {
	if (copy < newestCopy) {
		return Promise.resolve();
	}

	newestCopy = copy;
	stopWaiting();
	if (document.hasFocus()) {
		return navigator.clipboard.writeText('').catch(() => waitForFocus(copy));
	}

	waitForFocus(copy);
	return Promise.resolve();
}

export function forgetClearsBefore(copy) {
	newestCopy = Math.max(newestCopy, copy);
	if (waiting && waiting.copy < copy) {
		stopWaiting();
	}
}
