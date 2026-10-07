// Browser side of RemoteFileBrowser: keyboard shortcuts that must beat browser defaults (F5 reloads the page, Alt+Left
// navigates back, Ctrl+A selects the page), moving entries by dragging them onto folder rows, width steps for the
// responsive layout, focus and scroll helpers, Enter handling for the browser's dialogs, and the file editor's gutter,
// indenting and Ctrl+S.
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

// Tab inserts one tab rather than spaces: how a remote file is indented is the server's business, and one character is
// also the one thing Shift+Tab can take back exactly. Shift+Tab still lifts up to four leading spaces, which is the
// box's own tab-size, so a space-indented file outdents too.
const editorIndent = '\t';
const editorSpaces = 4;

// Never narrower than two digits, so a short file gets a gutter instead of a hairline.
const editorMinDigits = 2;

// How long a burst of caret moves is gathered before the gutter and .NET hear about it, about one frame. A timer and
// not requestAnimationFrame: a window the compositor is not painting still reports itself visible and still takes
// typing, but its animation frames never run, and the gutter would stop following the text.
const editorCoalesceMs = 16;

/** Where each line of `text` starts, so a caret offset becomes a line number without rescanning the file. */
function lineStarts(text) {
	const starts = [0];
	for (let index = text.indexOf('\n'); index >= 0; index = text.indexOf('\n', index + 1)) {
		starts.push(index + 1);
	}

	return starts;
}

/** The index in `starts` of the line that holds `offset`. */
function lineAt(starts, offset) {
	let low = 0;
	let high = starts.length - 1;
	while (low < high) {
		const middle = (low + high + 1) >> 1;
		if (starts[middle] <= offset) {
			low = middle;
		}
		else {
			high = middle - 1;
		}
	}

	return low;
}

/**
 * The file editor's box: fills and scrolls the line-number gutter, indents with Tab and outdents with Shift+Tab, saves
 * on Ctrl+S, and reports the caret through FileEditorJsReceiver. The numbers are written here and not rendered by .NET
 * because a thirty thousand line file would be that many nodes of render tree for something the page can print.
 * Dispose the result to detach.
 */
export function attachEditor(frame, area, numbers, dotNetRef) {
	const state = { dotNet: dotNetRef, disposed: false };
	let starts = [0];
	let stale = true;
	let drawn = 0;
	let reported = '';
	let pending = 0;
	// Escape arms the next Tab to move the focus instead of indenting, so the keyboard is never stuck in the box.
	let leaving = false;

	const measure = () => {
		if (!stale) {
			return;
		}

		stale = false;
		starts = lineStarts(area.value);
		if (starts.length === drawn) {
			return;
		}

		drawn = starts.length;
		const rows = new Array(drawn);
		for (let row = 0; row < drawn; row++) {
			rows[row] = row + 1;
		}

		numbers.textContent = rows.join('\n');
		frame.style.setProperty('--mt-edit-digits', String(Math.max(editorMinDigits, String(drawn).length)));
	};

	// The numbers ride a transform rather than a scrollTop of their own: a box with a horizontal scrollbar is shorter
	// inside than the gutter beside it, so a copied scrollTop would clamp before the last line.
	const follow = () => {
		numbers.style.transform = `translateY(${-area.scrollTop}px)`;
	};

	const report = () => {
		measure();
		const caret = area.selectionDirection === 'backward' ? area.selectionStart : area.selectionEnd;
		const line = lineAt(starts, caret);
		const column = caret - starts[line] + 1;
		const next = `${line}:${column}:${starts.length}`;
		if (next !== reported) {
			reported = next;
			invoke(state, 'OnCaretMoved', line + 1, column, starts.length);
		}
	};

	// One pass per burst, so holding an arrow key down costs one call and one gutter rebuild rather than one each.
	const refresh = () => {
		pending = 0;
		if (!state.disposed) {
			follow();
			report();
		}
	};

	const schedule = () => {
		if (!pending && !state.disposed) {
			pending = setTimeout(refresh, editorCoalesceMs);
		}
	};

	const edit = (text, selectionStart, selectionEnd) => {
		// insertText keeps the box's own undo stack and raises the input event Blazor listens for; assigning value
		// would throw both away. setRangeText is the fallback, and there the event has to be raised by hand.
		let inserted = false;
		try {
			inserted = document.execCommand('insertText', false, text);
		}
		catch {
			inserted = false;
		}

		if (!inserted) {
			area.setRangeText(text, area.selectionStart, area.selectionEnd, 'end');
			area.dispatchEvent(new Event('input', { bubbles: true }));
		}

		if (typeof selectionStart === 'number') {
			area.setSelectionRange(selectionStart, selectionEnd);
		}

		stale = true;
		schedule();
	};

	const indent = (outdent) => {
		measure();
		const value = area.value;
		const from = area.selectionStart;
		const to = area.selectionEnd;
		const firstLine = lineAt(starts, from);
		// A selection ending exactly on a line start belongs to the line above, not to the empty one it touches.
		const lastLine = to > from ? lineAt(starts, to - 1) : firstLine;
		if (!outdent && firstLine === lastLine) {
			edit(editorIndent);
			return;
		}

		const blockStart = starts[firstLine];
		const blockEnd = lastLine + 1 < starts.length ? starts[lastLine + 1] - 1 : value.length;
		const lines = value.slice(blockStart, blockEnd).split('\n');
		let head = 0;
		let total = 0;
		for (let index = 0; index < lines.length; index++) {
			const line = lines[index];
			let change = 0;
			if (!outdent) {
				// An empty line gets no indentation it would only have to lose again.
				if (line.length > 0) {
					lines[index] = editorIndent + line;
					change = editorIndent.length;
				}
			}
			else if (line.startsWith(editorIndent)) {
				lines[index] = line.slice(editorIndent.length);
				change = -editorIndent.length;
			}
			else {
				let spaces = 0;
				while (spaces < editorSpaces && line[spaces] === ' ') {
					spaces++;
				}

				if (spaces > 0) {
					lines[index] = line.slice(spaces);
					change = -spaces;
				}
			}

			if (index === 0) {
				head = change;
			}

			total += change;
		}

		if (total === 0) {
			return;
		}

		area.setSelectionRange(blockStart, blockEnd);
		edit(lines.join('\n'), Math.max(blockStart, from + head), Math.max(blockStart, to + total));
	};

	const onKeyDown = (event) => {
		if (event.isComposing) {
			return;
		}

		if ((event.ctrlKey || event.metaKey) && !event.altKey && event.key.length === 1 && event.key.toLowerCase() === 's') {
			event.preventDefault();
			event.stopPropagation();
			// The text rides along: an input event still on its way to .NET would otherwise have it save the copy from
			// before the last keystroke.
			invoke(state, 'OnSaveRequested', area.value);
			return;
		}

		if (event.target !== area) {
			return;
		}

		if (event.key === 'Escape') {
			leaving = true;
			return;
		}

		if (event.key === 'Tab' && !event.ctrlKey && !event.metaKey && !event.altKey) {
			if (leaving) {
				leaving = false;
				return;
			}

			event.preventDefault();
			indent(event.shiftKey);
			return;
		}

		leaving = false;
	};

	const onInput = () => {
		stale = true;
		schedule();
	};

	// Ctrl+S belongs to the whole dialog and not only to the box, because Escape then Tab is how the keyboard leaves
	// the box and the Save button it lands on is outside it.
	const keys = frame.closest('.moka-dialog') || frame;
	const selectionEvents = ['selectionchange', 'keyup', 'pointerup', 'focus'];
	keys.addEventListener('keydown', onKeyDown);
	area.addEventListener('input', onInput);
	area.addEventListener('scroll', follow, { passive: true });
	for (const type of selectionEvents) {
		area.addEventListener(type, schedule);
	}

	measure();
	follow();
	// The first caret report waits for the timer, so nothing calls .NET while it still awaits this attach.
	schedule();

	return {
		dispose() {
			state.disposed = true;
			clearTimeout(pending);
			keys.removeEventListener('keydown', onKeyDown);
			area.removeEventListener('input', onInput);
			area.removeEventListener('scroll', follow);
			for (const type of selectionEvents) {
				area.removeEventListener(type, schedule);
			}
		},
	};
}

