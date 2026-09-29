// Browser side of RemoteFileBrowser: keyboard shortcuts that must beat browser defaults (F5 reloads the page, Alt+Left
// navigates back, Ctrl+A selects the page), moving entries by dragging them onto folder rows, width steps for the
// responsive layout, focus and scroll helpers, and Enter handling for the browser's dialogs.
import { invoke } from '../../Mokaterm.UI.Common/js/interop.js';
export { bindDialog } from '../../Mokaterm.UI.Common/js/forms.js';

const entryType = 'application/x-mokaterm-entry';
const dropTargetClass = 'fb-drop-target';
const typeAheadResetMs = 800;
// Long enough for a closing menu to hand focus back over a slow circuit; any key or click ends it sooner.

function isElement(target) {
	return !!target && typeof target.closest === 'function';
}

function isEditable(target) {
	return isElement(target) && (target.isContentEditable || !!target.closest('input, textarea, select'));
}

// Enter and Space belong to a focused control.
function isActivatable(target) {
	return isElement(target)
		&& !!target.closest('button, a[href], [role="button"], [role="tab"], [role="menuitem"], [role="checkbox"], [role="switch"], [role="radio"]');
}

function commandFor(event) {
	const mod = event.ctrlKey || event.metaKey;
	const alt = event.altKey;
	switch (event.key) {
		case 'Enter':
			return mod || alt || isActivatable(event.target) ? null : 'open';
		case ' ':
			return mod || alt || isActivatable(event.target) ? null : 'toggle';
		case 'Backspace':
			return mod || alt ? null : 'up';
		case 'F2':
			return 'rename';
		case 'F4':
			return 'edit';
		case 'Delete':
			return mod || alt ? null : 'delete';
		case 'F5':
			return 'refresh';
		case 'Escape':
			return 'clear';
		case 'ArrowLeft':
			return alt && !mod ? 'back' : null;
		case 'ArrowRight':
			return alt && !mod ? 'forward' : null;
		case 'ArrowUp':
			return mod ? null : alt ? 'up' : 'previous';
		case 'ArrowDown':
			return mod || alt ? null : 'next';
		case 'Home':
			return mod || alt ? null : 'first';
		case 'End':
			return mod || alt ? null : 'last';
		case 'PageUp':
			return mod || alt ? null : 'pageUp';
		case 'PageDown':
			return mod || alt ? null : 'pageDown';
		default:
			if (mod && !alt && event.key.length === 1) {
				switch (event.key.toLowerCase()) {
					case 'a':
						return 'selectAll';
					case 'l':
						return 'editPath';
					case 'f':
						return 'filter';
					default:
						return null;
				}
			}

			return null;
	}
}

export function attachBrowser(root, dotNetRef, initiallyActive, widthSteps) {
	let active = !!initiallyActive;
	let typeAhead = '';
	let typeAheadTimer = 0;
	let dragSource = null;
	let dropRow = null;
	let widthStep = -1;
	const state = { dotNet: dotNetRef, disposed: false };

	const scroller = () => root.querySelector('.moka-table-container');

	const rowHeight = () => {
		const row = root.querySelector('tbody tr[data-row-index]');
		return row ? row.getBoundingClientRect().height : 0;
	};

	const pageRows = () => {
		const container = scroller();
		const height = rowHeight();
		return container && height > 0 ? Math.max(1, Math.floor(container.clientHeight / height) - 1) : 10;
	};

	const onKeyDown = event => {
		if (!active || event.isComposing || isEditable(event.target)) {
			return;
		}

		const mod = event.ctrlKey || event.metaKey;
		const typing = !mod && !event.altKey && event.key.length === 1;
		if (typing && (event.key !== ' ' || typeAhead.length > 0) && !(event.key === ' ' && isActivatable(event.target))) {
			typeAhead += event.key;
			clearTimeout(typeAheadTimer);
			typeAheadTimer = setTimeout(() => { typeAhead = ''; }, typeAheadResetMs);
			event.preventDefault();
			event.stopPropagation();
			invoke(state, 'OnTypeAhead', typeAhead);
			return;
		}

		const command = commandFor(event);
		if (!command) {
			return;
		}

		// Escape has no browser default worth blocking, and outer handlers may still want it.
		if (command !== 'clear') {
			event.preventDefault();
			event.stopPropagation();
		}

		invoke(state, 'OnKeyCommand', command, event.shiftKey, pageRows());
	};

	const rowEntry = target => {
		const row = isElement(target) ? target.closest('tr') : null;
		if (!row || !root.contains(row)) {
			return null;
		}

		const cell = row.querySelector('[data-entry-path]');
		return cell
			? { row, path: cell.getAttribute('data-entry-path'), isDirectory: cell.getAttribute('data-entry-directory') === 'true' }
			: null;
	};

	const carriesEntry = event => {
		const types = event.dataTransfer?.types;
		return !!types && Array.from(types).includes(entryType);
	};

	const setDropRow = row => {
		if (dropRow === row) {
			return;
		}

		dropRow?.classList.remove(dropTargetClass);
		dropRow = row;
		dropRow?.classList.add(dropTargetClass);
	};

	const onDragStart = event => {
		const handle = isElement(event.target) ? event.target.closest('[draggable="true"]') : null;
		const entry = handle ? rowEntry(handle) : null;
		if (!entry || !event.dataTransfer) {
			return;
		}

		dragSource = entry.path;
		// Firefox only starts a drag that carries data.
		event.dataTransfer.setData(entryType, entry.path);
		event.dataTransfer.setData('text/plain', entry.path);
		event.dataTransfer.effectAllowed = 'move';
	};

	const onDragOver = event => {
		if (!dragSource || !carriesEntry(event)) {
			return;
		}

		event.stopPropagation();
		const entry = rowEntry(event.target);
		if (!entry || !entry.isDirectory || entry.path === dragSource) {
			setDropRow(null);
			return;
		}

		event.preventDefault();
		event.dataTransfer.dropEffect = 'move';
		setDropRow(entry.row);
	};

	const onDragLeave = event => {
		if (dragSource && !root.contains(event.relatedTarget)) {
			setDropRow(null);
		}
	};

	const onDrop = event => {
		if (!dragSource || !carriesEntry(event)) {
			return;
		}

		const source = dragSource;
		const entry = rowEntry(event.target);
		dragSource = null;
		setDropRow(null);
		if (!entry || !entry.isDirectory || entry.path === source) {
			return;
		}

		event.preventDefault();
		event.stopPropagation();
		invoke(state, 'OnEntryDrop', source, entry.path);
	};

	const onDragEnd = () => {
		dragSource = null;
		setDropRow(null);
	};

	// Only crossing a step reaches .NET, so dragging a panel splitter costs a call per step, not per frame. A hidden
	// browser (display: none in a background tab) measures 0 and keeps the layout it had.
	const steps = Array.isArray(widthSteps) ? widthSteps : [];
	const resizeObserver = new ResizeObserver(entries => {
		const width = entries[entries.length - 1].contentRect.width;
		if (width <= 0) {
			return;
		}

		const step = steps.filter(minimum => width >= minimum).length;
		if (step !== widthStep) {
			widthStep = step;
			invoke(state, 'OnWidthChanged', step);
		}
	});
	resizeObserver.observe(root);

	// Capture, so the browser's keys run before the table's own. MokaTable clicks its focused cell on Enter, and that
	// cell is the one a mouse click focused, not the one the arrow keys moved the selection to since.
	root.addEventListener('keydown', onKeyDown, true);
	root.addEventListener('dragstart', onDragStart);
	root.addEventListener('dragover', onDragOver);
	root.addEventListener('dragleave', onDragLeave);
	root.addEventListener('drop', onDrop);
	root.addEventListener('dragend', onDragEnd);

	return {
		setActive(value) {
			active = !!value;
			if (!active) {
				typeAhead = '';
			}
		},

		// Keeps the row at index visible below the sticky header, and moves DOM focus onto it when focus is already in
		// the browser so the focus ring follows the keyboard. Rows the virtualized table has not rendered are estimated.
		scrollToRow(index) {
			const container = scroller();
			if (!container || index < 0) {
				return;
			}

			const row = container.querySelector(`tbody tr[data-row-index="${index}"]`);
			const header = container.querySelector('thead');
			const headerHeight = header ? header.getBoundingClientRect().height : 0;
			if (!row) {
				const height = rowHeight() || 25;
				container.scrollTop = Math.max(0, index * height - (container.clientHeight - headerHeight) / 2);
				return;
			}

			const box = container.getBoundingClientRect();
			const rect = row.getBoundingClientRect();
			if (rect.top < box.top + headerHeight) {
				container.scrollTop -= box.top + headerHeight - rect.top;
			} else if (rect.bottom > box.bottom) {
				container.scrollTop += rect.bottom - box.bottom;
			}

			const cell = row.querySelector('td[data-col-index]');
			if (cell && root.contains(document.activeElement)) {
				cell.focus({ preventScroll: true });
			}
		},

		// After a navigation the focused row may be gone; bring focus back so shortcuts keep working.
		restoreFocus() {
			const current = document.activeElement;
			if (!current || current === document.body || !current.isConnected) {
				root.focus({ preventScroll: true });
			}
		},

		focus(selector) {
			const element = root.querySelector(selector);
			if (element) {
				element.focus();
				if (typeof element.select === 'function') {
					element.select();
				}
			}
		},

		dispose() {
			state.disposed = true;
			resizeObserver.disconnect();
			root.removeEventListener('keydown', onKeyDown, true);
			root.removeEventListener('dragstart', onDragStart);
			root.removeEventListener('dragover', onDragOver);
			root.removeEventListener('dragleave', onDragLeave);
			root.removeEventListener('drop', onDrop);
			root.removeEventListener('dragend', onDragEnd);
			clearTimeout(typeAheadTimer);
			setDropRow(null);
		},
	};
}

// Keeps the highlighted option of a listbox in view while the keyboard moves through it.
export function scrollSelectedIntoView(list) {
	list?.querySelector('[aria-selected="true"]')?.scrollIntoView({ block: 'nearest' });
}

