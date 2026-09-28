// Mokaterm shell interop: global shortcuts, idle reporting, the leave-page prompt, tab strip menus, and focus and Enter
// handling for forms.
import { attachOnce, invoke } from '../../Mokaterm.UI.Common/js/interop.js';

const shells = new Map();
let nextShellHandle = 0;

// On macOS the app shortcuts use Cmd, so Ctrl combinations stay free for the terminal. Tab switching keeps Ctrl because
// Cmd+Tab belongs to the operating system.
const isMac = /Mac|iPhone|iPad/.test(navigator.platform || '');

// Letters are matched by the character they type so shortcuts follow the keyboard layout. Keys that do not type a
// Latin letter (Tab, Comma, Cyrillic layouts) fall back to the physical key code.
function keyId(event) {
	if (typeof event.key === 'string' && event.key.length === 1) {
		const upper = event.key.toUpperCase();
		if (upper >= 'A' && upper <= 'Z') {
			return 'Key' + upper;
		}
	}

	return event.code;
}

function findShortcut(shortcuts, event) {
	// AltGr is reported as Ctrl+Alt on Windows; those keystrokes type characters and must never become shortcuts.
	if (event.isComposing || event.altKey || (event.getModifierState && event.getModifierState('AltGraph'))) {
		return null;
	}

	const id = keyId(event);
	for (const shortcut of shortcuts) {
		if (shortcut.code !== id || shortcut.shift !== event.shiftKey) {
			continue;
		}

		const useMeta = isMac && shortcut.code !== 'Tab';
		const primary = useMeta ? event.metaKey : event.ctrlKey;
		const other = useMeta ? event.ctrlKey : event.metaKey;
		if (primary && !other) {
			return shortcut;
		}
	}

	return null;
}

export function attachShell(dotNetRef, options) {
	const handle = ++nextShellHandle;
	const state = { dotNet: dotNetRef, disposed: false };
	const shortcuts = options.shortcuts || [];
	const activityIntervalMs = options.activityIntervalMs || 20000;
	let lastActivity = 0;

	const reportActivity = () => {
		const now = Date.now();
		if (now - lastActivity < activityIntervalMs) {
			return;
		}

		lastActivity = now;
		invoke(state, 'OnActivity');
	};

	// Capture phase on window runs before xterm.js sees the key. Only the configured combinations are consumed;
	// everything else, including plain Ctrl+letter keys, continues to the terminal untouched.
	// Off while the settings page records a new gesture, so the keys being pressed do not run the commands they
	// currently belong to.
	let shortcutsEnabled = true;
	const onKeyDown = (event) => {
		reportActivity();
		const shortcut = shortcutsEnabled ? findShortcut(shortcuts, event) : null;
		if (!shortcut) {
			return;
		}

		event.preventDefault();
		event.stopPropagation();
		if (!event.repeat || shortcut.code === 'Tab') {
			invoke(state, 'OnShortcut', shortcut.id);
		}
	};

	const onPointer = () => reportActivity();

	const onVisibilityChange = () => {
		if (document.visibilityState === 'hidden') {
			invoke(state, 'OnHidden');
		}
	};

	// Firefox only starts an HTML drag when data is set, which Blazor's drag events cannot do.
	const onDragStart = (event) => {
		const source = event.target instanceof Element ? event.target.closest('[data-mt-drag]') : null;
		if (source && event.dataTransfer) {
			event.dataTransfer.setData('application/x-mokaterm-item', source.getAttribute('data-mt-drag'));
			event.dataTransfer.effectAllowed = 'move';
		}
	};

	// The browser shows its own "leave site?" prompt; the text cannot be customized.
	let confirmLeave = false;
	const onBeforeUnload = (event) => {
		if (confirmLeave) {
			event.preventDefault();
			event.returnValue = '';
		}
	};

	window.addEventListener('keydown', onKeyDown, true);
	window.addEventListener('pointerdown', onPointer, true);
	window.addEventListener('wheel', onPointer, { capture: true, passive: true });
	document.addEventListener('dragstart', onDragStart);
	if (options.watchVisibility) {
		document.addEventListener('visibilitychange', onVisibilityChange);
	}

	if (options.confirmLeave) {
		window.addEventListener('beforeunload', onBeforeUnload);
	}

	shells.set(handle, {
		setConfirmLeave: (value) => {
			confirmLeave = !!value;
		},
		setShortcutsEnabled: (value) => {
			shortcutsEnabled = !!value;
		},
		detach: () => {
			state.disposed = true;
			window.removeEventListener('keydown', onKeyDown, true);
			window.removeEventListener('pointerdown', onPointer, true);
			window.removeEventListener('wheel', onPointer, true);
			window.removeEventListener('beforeunload', onBeforeUnload);
			document.removeEventListener('dragstart', onDragStart);
			document.removeEventListener('visibilitychange', onVisibilityChange);
		},
	});

	return handle;
}

export function detachShell(handle) {
	const shell = shells.get(handle);
	if (shell) {
		shell.detach();
		shells.delete(handle);
	}
}

export function setConfirmLeave(handle, value) {
	shells.get(handle)?.setConfirmLeave(value);
}

export function setShortcutsEnabled(handle, value) {
	shells.get(handle)?.setShortcutsEnabled(value);
}

export function attachListKeys(element) {
	attachOnce(element, 'listKeys', (target) => {
		target.addEventListener('keydown', (event) => {
			if (event.target === target && ['ArrowUp', 'ArrowDown', 'Home', 'End', 'PageUp', 'PageDown', ' '].includes(event.key)) {
				event.preventDefault();
			}
		});
	});
}

export function scrollActiveIntoView(element) {
	const active = element ? element.querySelector('.moka-list-item--active') : null;
	if (active) {
		active.scrollIntoView({ block: 'nearest' });
	}
}
