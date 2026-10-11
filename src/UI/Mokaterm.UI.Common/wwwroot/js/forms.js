// Starting focus and Enter for forms and dialogs, shared by the shell, the settings pages and the file browser.
import { attachOnce } from './interop.js';

// Enter belongs to whatever already handles it: a multi-line field needs it for typing, and a focused button, link or
// picker answers with itself, so Enter on a focused Cancel cancels whatever the default button is.
const keepsEnter = 'textarea, button, a, select, [role="combobox"], [role="listbox"], [data-no-submit]';

// How long focus is held against a late focus-restore, and how often it is checked. A context menu or the palette puts
// focus back where it was when it closes, which can land after the dialog opened, so focus is taken back until the user
// types or clicks. The poll is what covers a page whose window is in the background: it fires no focus events at all,
// which is how a prompt raised during a reconnect used to open somewhere else.
const holdMs = 1500;
const pollMs = 50;
const maxRedirects = 3;

function holdFocus(target) {
	if (!(target instanceof HTMLElement)) {
		return () => {};
	}

	let redirects = 0;
	let poll = 0;
	let timer = 0;
	const release = () => {
		document.removeEventListener('focusin', onFocusIn, true);
		document.removeEventListener('pointerdown', release, true);
		document.removeEventListener('keydown', onKeyDown, true);
		clearInterval(poll);
		clearTimeout(timer);
	};

	const reclaim = () => {
		if (!target.isConnected) {
			release();
			return;
		}

		if (document.activeElement === target) {
			return;
		}

		if (++redirects > maxRedirects) {
			release();
			return;
		}

		target.focus();
	};

	const onFocusIn = (event) => {
		if (event.target !== target) {
			reclaim();
		}
	};

	// A held key repeats into whatever it opened; that is the key that opened this, not the user taking over.
	const onKeyDown = (event) => {
		if (!event.repeat) {
			release();
		}
	};

	document.addEventListener('focusin', onFocusIn, true);
	document.addEventListener('pointerdown', release, true);
	document.addEventListener('keydown', onKeyDown, true);
	poll = setInterval(reclaim, pollMs);
	timer = setTimeout(release, holdMs);
	target.focus();
	return release;
}

function findTarget(root, selectors) {
	for (const selector of selectors) {
		const target = root.querySelector(selector);
		if (target instanceof HTMLElement) {
			return target;
		}
	}

	return null;
}

/**
 * Starting focus and Enter for one form or dialog. Returns a function that detaches everything.
 * Options: focus (selectors, first match wins), submit (the default button's selector), submitOnEnter, selectText,
 * selectionStart and selectionEnd.
 */
function bindForm(root, options) {
	const settings = options || {};
	const target = findTarget(root, settings.focus || ['[data-autofocus]']);
	const listeners = [];
	if (settings.submitOnEnter) {
		const onKeyDown = (event) => {
			// A held Enter repeats out of the action that opened this, and must not answer it.
			if (event.repeat) {
				const origin = event.target instanceof Element ? event.target : null;
				if (event.key === 'Enter' && !(origin && origin.closest('textarea'))) {
					event.preventDefault();
				}

				return;
			}

			if (event.key !== 'Enter' || event.isComposing || event.shiftKey || event.ctrlKey || event.altKey || event.metaKey) {
				return;
			}

			const origin = event.target instanceof Element ? event.target : null;
			if (origin && origin.closest(keepsEnter)) {
				return;
			}

			const submit = root.querySelector(settings.submit || '[data-primary]');
			if (submit instanceof HTMLButtonElement && !submit.disabled) {
				event.preventDefault();
				event.stopPropagation();
				submit.click();
			}
		};

		root.addEventListener('keydown', onKeyDown);
		listeners.push(() => root.removeEventListener('keydown', onKeyDown));
	}

	const release = holdFocus(target);
	if (target) {
		if (typeof settings.selectionStart === 'number' && typeof settings.selectionEnd === 'number'
			&& typeof target.setSelectionRange === 'function') {
			target.setSelectionRange(settings.selectionStart, settings.selectionEnd);
		}
		else if (settings.selectText && typeof target.select === 'function') {
			target.select();
		}
	}

	return () => {
		release();
		for (const remove of listeners) {
			remove();
		}
	};
}

export function focusPreferred(root, selectText) {
	if (root) {
		bindForm(root, { selectText });
	}
}

export function prepareForm(anchor, options) {
	if (!anchor) {
		return;
	}

	const root = options && options.dialog ? (anchor.closest('.moka-dialog') || anchor) : anchor;

	// The Enter handler belongs to the dialog and outlives this call; the focus hold is per request, because a second
	// prompt in the same dialog has to take focus again.
	attachOnce(root, 'form', (target) => {
		bindForm(target, { submitOnEnter: options && options.submitOnEnter, focus: [] });
	});

	bindForm(root, { selectText: false });
}

/**
 * The file browser's dialogs: the field marked data-dialog-focus starts focused with the given range selected, or the
 * default button does, so the focus ring shows what Enter will do. Dispose the result to detach.
 */
export function bindDialog(element, selectionStart, selectionEnd) {
	const dispose = bindForm(element, {
		focus: ['[data-dialog-focus]', '[data-dialog-default]:not([disabled])'],
		submit: '[data-dialog-default]',
		submitOnEnter: true,
		selectionStart,
		selectionEnd,
	});

	return { dispose };
}
