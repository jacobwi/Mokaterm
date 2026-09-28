// Clears a copied secret from the clipboard. Browsers and WebView2 refuse clipboard writes while the page is not
// focused, which is exactly when a copied password is being pasted into another window, so a page in the background
// clears as soon as it is focused again instead of leaving the secret behind.

let clearOnFocus = null;

function waitForFocus() {
	if (clearOnFocus) {
		return;
	}

	clearOnFocus = () => {
		clearOnFocus = null;
		navigator.clipboard.writeText('').catch(() => {
			// Still refused (no permission at all); nothing more a page can do.
		});
	};
	window.addEventListener('focus', clearOnFocus, { once: true });
}

export function clearClipboard() {
	if (clearOnFocus) {
		window.removeEventListener('focus', clearOnFocus);
		clearOnFocus = null;
	}

	if (document.hasFocus()) {
		return navigator.clipboard.writeText('').catch(() => waitForFocus());
	}

	waitForFocus();
	return Promise.resolve();
}
