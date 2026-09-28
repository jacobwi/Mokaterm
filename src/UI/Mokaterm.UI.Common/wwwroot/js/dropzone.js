// The bookkeeping every OS file drop needs, shared by the browser bridge and the desktop one. What differs is only how
// the dropped files are taken: the browser keeps the File objects and streams them later, the desktop hands them to
// WebView2 so .NET gets real paths.

// dragleave is not always delivered when a drag leaves the window; a hover with no dragover events has ended, and
// without this the drop overlay stays on screen until the next drag.
const hoverTimeoutMs = 1000;

export function carriesFiles(event) {
	const types = event.dataTransfer?.types;
	return !!types && Array.from(types).includes('Files');
}

export function newDropId() {
	if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
		return crypto.randomUUID();
	}

	// randomUUID needs a secure context; a plain-http host still works with a less random id.
	return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}

/**
 * Watches one element for an OS file drop. `onOverChanged(bool)` is told when the overlay should show, and `onDrop`
 * runs while the drop event is still live, which is the only moment its DataTransfer can be read. Dispose to detach.
 */
export function attachDropZone(element, onOverChanged, onDrop) {
	let depth = 0;
	let over = false;
	let lastOverAt = 0;
	let watchdog = 0;

	const setOver = (value) => {
		if (over === value) {
			return;
		}

		over = value;
		if (value) {
			watchdog = setInterval(() => {
				if (Date.now() - lastOverAt > hoverTimeoutMs) {
					depth = 0;
					setOver(false);
				}
			}, hoverTimeoutMs);
		}
		else {
			clearInterval(watchdog);
			watchdog = 0;
		}

		onOverChanged(value);
	};

	const onEnter = (event) => {
		if (!carriesFiles(event)) {
			return;
		}

		event.preventDefault();
		depth++;
		lastOverAt = Date.now();
		setOver(true);
	};

	const onOver = (event) => {
		if (!carriesFiles(event)) {
			return;
		}

		// Without preventDefault the drop is refused and the view navigates to the file instead. dragover fires every
		// few milliseconds; stopping it here keeps each one out of the table's own drag handlers.
		event.preventDefault();
		event.stopPropagation();
		event.dataTransfer.dropEffect = 'copy';
		lastOverAt = Date.now();
		setOver(true);
	};

	const onLeave = (event) => {
		if (!carriesFiles(event)) {
			return;
		}

		depth = Math.max(0, depth - 1);
		if (depth === 0) {
			setOver(false);
		}
	};

	const onDropped = (event) => {
		if (!carriesFiles(event)) {
			return;
		}

		event.preventDefault();
		event.stopPropagation();
		depth = 0;
		setOver(false);
		onDrop(event);
	};

	element.addEventListener('dragenter', onEnter);
	element.addEventListener('dragover', onOver);
	element.addEventListener('dragleave', onLeave);
	element.addEventListener('drop', onDropped);

	return {
		dispose() {
			element.removeEventListener('dragenter', onEnter);
			element.removeEventListener('dragover', onOver);
			element.removeEventListener('dragleave', onLeave);
			element.removeEventListener('drop', onDropped);
			clearInterval(watchdog);
			watchdog = 0;
			over = false;
			depth = 0;
		},
	};
}
