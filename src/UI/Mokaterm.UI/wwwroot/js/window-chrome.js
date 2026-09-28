// Window chrome for a desktop window without a system title bar: the shell's top bar stands in for it. The host needs to
// know which parts of the bar move the window, so this reports the bar's empty stretches whenever its layout changes, in
// CSS pixels from the top left of the page. Every control keeps its whole column, above and below it included, so a
// click that misses a button by a pixel does nothing instead of moving the window.

// Everything that takes a click. A form field counts as its whole box, so its icon and padding still focus it.
const interactive = [
	'button', 'a[href]', 'input', 'select', 'textarea', 'label',
	'[tabindex]:not([tabindex="-1"])', '[contenteditable]:not([contenteditable="false"])',
	'[role="button"]', '[role="combobox"]', '[role="listbox"]', '[role="menu"]', '[role="option"]', '[role="tab"]',
].join(',');

// A narrower gap between two controls stays with the page.
const minimumWidth = 8;

function shown(element) {
	if (element.getClientRects().length === 0) {
		return false;
	}

	const style = getComputedStyle(element);
	return style.visibility !== 'hidden' && style.pointerEvents !== 'none';
}

// The shell writes the width of the window's own buttons into these properties.
function reserved(root, name) {
	const value = parseFloat(getComputedStyle(root).getPropertyValue(name));
	return Number.isFinite(value) && value > 0 ? value : 0;
}

// An open dropdown lays a backdrop over the whole window to catch the click that closes it. While one is open the bar
// belongs to the page, or that click would move the window and leave the dropdown open.
function hasOpenOverlay(root) {
	if (root.querySelector('[aria-expanded]:not([aria-expanded="false"])')) {
		return true;
	}

	for (const element of root.querySelectorAll('*')) {
		if (getComputedStyle(element).position === 'fixed' && shown(element)) {
			return true;
		}
	}

	return false;
}

function measure(root) {
	const bounds = root.getBoundingClientRect();
	const top = Math.max(0, Math.ceil(bounds.top));
	const bottom = Math.floor(bounds.bottom);
	const left = Math.ceil(Math.max(bounds.left, reserved(root, '--mt-chrome-left')));
	const right = Math.floor(Math.min(bounds.right, document.documentElement.clientWidth - reserved(root, '--mt-chrome-right')));
	if (bottom - top < 1 || right - left < minimumWidth || hasOpenOverlay(root)) {
		return [];
	}

	const taken = [];
	for (const element of root.querySelectorAll(interactive)) {
		if (!shown(element)) {
			continue;
		}

		const field = element.closest('.moka-field');
		const rect = (field && root.contains(field) ? field : element).getBoundingClientRect();
		if (rect.right > left && rect.left < right && rect.bottom > top && rect.top < bottom) {
			taken.push([Math.max(left, Math.floor(rect.left)), Math.min(right, Math.ceil(rect.right))]);
		}
	}

	taken.sort((a, b) => a[0] - b[0]);
	const regions = [];
	let x = left;
	const addUpTo = (end) => {
		if (end - x >= minimumWidth) {
			regions.push({ x, y: top, width: end - x, height: bottom - top });
		}
	};

	for (const [start, end] of taken) {
		addUpTo(start);
		x = Math.max(x, end);
	}

	addUpTo(right);
	return regions;
}

export function attach(root, dotNetRef) {
	let frame = 0;
	let last = null;
	let disposed = false;

	const report = () => {
		frame = 0;
		if (disposed || !root.isConnected) {
			return;
		}

		const regions = measure(root);

		// The pixel ratio is part of the key: on a screen with another scale the layout stays the same, but the host has
		// to place the regions again.
		const key = JSON.stringify(regions) + '@' + window.devicePixelRatio;
		if (key === last) {
			return;
		}

		last = key;
		dotNetRef.invokeMethodAsync('OnDragRegionsChanged', regions).catch(() => {
			// The page is closing; there is no window left to tell.
		});
	};

	const schedule = () => {
		if (!frame && !disposed) {
			frame = requestAnimationFrame(report);
		}
	};

	// Controls change size without the bar changing size: a count on the transfers badge, a longer protocol name.
	const resizeObserver = new ResizeObserver(schedule);
	const observeParts = () => {
		resizeObserver.disconnect();
		resizeObserver.observe(root);
		for (const element of root.querySelectorAll(interactive)) {
			resizeObserver.observe(element);
		}
	};

	// Renders of the bar, a dropdown opening and the inset properties changing on the root all land here.
	const mutationObserver = new MutationObserver(() => {
		observeParts();
		schedule();
	});

	let resolution = null;
	const onResolutionChange = () => {
		watchResolution();
		schedule();
	};
	const watchResolution = () => {
		resolution?.removeEventListener('change', onResolutionChange);
		resolution = matchMedia(`(resolution: ${window.devicePixelRatio}dppx)`);
		resolution.addEventListener('change', onResolutionChange);
	};

	mutationObserver.observe(root, { subtree: true, childList: true, attributes: true, characterData: true });
	window.addEventListener('resize', schedule);
	document.fonts?.addEventListener('loadingdone', schedule);
	watchResolution();
	observeParts();
	schedule();

	return {
		dispose() {
			disposed = true;
			if (frame) {
				cancelAnimationFrame(frame);
				frame = 0;
			}

			resizeObserver.disconnect();
			mutationObserver.disconnect();
			window.removeEventListener('resize', schedule);
			document.fonts?.removeEventListener('loadingdone', schedule);
			resolution?.removeEventListener('change', onResolutionChange);
		},
	};
}
