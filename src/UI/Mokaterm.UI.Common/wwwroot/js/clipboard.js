// The page's clipboard writes, and the clear that follows a copied secret.
//
// Writes start inside the click. Safari, iOS and Firefox let a page write the clipboard only while the click that asked
// for it is still live, and on Blazor Server the .NET handler runs a round trip later. So a click on an element with the
// class mt-copy-click (CopyClick.Class in .NET) starts a write at once, a ClipboardItem whose text is a promise (Chromium
// 98, Firefox 127 and Safari 13.1 take one), and the next text .NET hands to writeText fills it. A write nobody fills
// within AskLimitMs fails and leaves the clipboard as it was. Page scripts that copy inside their own key or mouse
// handler use copyText, or expectCopy when .NET supplies the text.
//
// Clears: browsers and WebView2 refuse clipboard writes while the page is not focused, which is exactly when a copied
// password is being pasted into another window, so a page in the background clears as soon as it is focused again
// instead of leaving the secret behind.
//
// The deadline lives in .NET (ClipboardClearSchedule) and is shared by a window and its tool windows, so the clear for
// one copy can reach this page after a newer copy was made in another window. Every copy is numbered: a clear for a copy
// older than the newest this page has heard of does nothing, whether it is still waiting for focus or arrives late.
//
// .NET numbers a copy only once its handler ran, which for a write made inside the click is a round trip after the
// clipboard changed. So this page numbers its own successful writes as well, .NET hands a secret's number back with its
// clear, and a page that wrote anything after that secret has nothing of it left to clear.
//
// State lives on the document, so a second instance of this module (the same file under another URL) shares it.

const StateKey = Symbol.for('mokaterm.clipboard');
const AskClass = 'mt-copy-click';
// Below ClipboardClearSchedule.PageAnswerLimit (5 s): a clear waits for unfilled writes, and .NET stops waiting for a page
// after that limit.
const AskLimitMs = 4000;

function state() {
	document[StateKey] ??= {
		listening: false,
		written: 0,
		newestCopy: 0,
		waiting: null,
		asks: [],
		pending: new Set(),
	};
	return document[StateKey];
}

/** Installs the document's listeners once; the JavaScript initializer calls it at startup. */
export function listenForCopies() {
	const s = state();
	if (s.listening) {
		return;
	}

	s.listening = true;

	// Capturing, so a handler that stops the event on its way up cannot stop the write.
	document.addEventListener('click', onClick, true);
	document.addEventListener('keydown', onKeyDown, true);
}

function onClick(event) {
	if (marked(event.target instanceof Element ? event.target.closest('.' + AskClass) : null)) {
		expectCopy();
	}
}

// A menu keeps the focus on itself and names its current item with aria-activedescendant, so Enter on an item is a key
// press on the menu and never a click. A button's Enter is left to the click it turns into.
function onKeyDown(event) {
	if ((event.key !== 'Enter' && event.key !== ' ') || event.repeat || event.isComposing || !(event.target instanceof Element)) {
		return;
	}

	const id = event.target.getAttribute('aria-activedescendant');
	if (id && marked(document.getElementById(id)?.closest('.' + AskClass))) {
		expectCopy();
	}
}

function marked(element) {
	return !!element && !element.matches(':disabled') && element.getAttribute('aria-disabled') !== 'true';
}

/**
 * Starts a write whose text .NET sends next through writeText. Call it inside the click or key press that will copy.
 * Without ClipboardItem it does nothing, and the write .NET makes later is the only one.
 */
export function expectCopy() {
	if (!window.isSecureContext || typeof ClipboardItem !== 'function' || typeof navigator.clipboard?.write !== 'function') {
		return;
	}

	const s = state();
	let fill;
	let refuse;
	const text = new Promise((resolve, reject) => {
		fill = resolve;
		refuse = reject;
	});
	const blob = text.then(value => new Blob([value], { type: 'text/plain' }));
	blob.catch(() => { });

	let written;
	try {
		written = navigator.clipboard.write([new ClipboardItem({ 'text/plain': blob })]);
	}
	catch {
		// Chromium before 98 takes no promise in a ClipboardItem.
		return;
	}

	const ask = { fill: null, result: null };
	const timer = setTimeout(() => {
		s.asks = s.asks.filter(other => other !== ask);
		refuse(new Error('No text arrived to copy.'));
	}, AskLimitMs);
	ask.fill = value => {
		clearTimeout(timer);
		fill(value);
	};
	ask.result = track(s, written.then(() => ++s.written));
	s.asks.push(ask);
}

/**
 * Puts text on the clipboard for .NET, filling the oldest write a click started where there is one.
 * @returns {Promise<number>} The page's number for the write; rejects when the browser refused it.
 */
export function writeText(text) {
	const s = state();
	const ask = s.asks.shift();
	if (!ask) {
		return write(s, text);
	}

	ask.fill(text);

	// The browser may still refuse once the text arrives (the page lost focus meanwhile); a plain write gets one more try.
	return ask.result.catch(() => write(s, text));
}

/**
 * Copies inside the caller's own key or mouse handler, for page scripts that have the text themselves.
 * @param {string} text - The text to copy.
 * @param {{ command?: boolean }} [options] - command: false leaves out the fallback that selects hidden text, for a
 *   write no user asked for, which that fallback cannot make and whose blur could end an edit in progress.
 * @returns {Promise<boolean>} True when the text reached the clipboard.
 */
export function copyText(text, { command = true } = {}) {
	const s = state();
	if (typeof navigator.clipboard?.writeText === 'function') {
		return write(s, text).then(() => true, () => command && copyWithCommand(s, text));
	}

	return Promise.resolve(command && copyWithCommand(s, text));
}

function write(s, text) {
	return track(s, navigator.clipboard.writeText(text).then(() => ++s.written));
}

function track(s, promise) {
	s.pending.add(promise);
	const done = () => s.pending.delete(promise);
	promise.then(done, done);
	return promise;
}

async function settle(s) {
	while (s.pending.size > 0) {
		await Promise.allSettled([...s.pending]);
	}
}

// navigator.clipboard only exists in secure contexts, and a web host served over plain HTTP is not one.
function copyWithCommand(s, text) {
	const focused = document.activeElement;
	const textarea = document.createElement('textarea');
	textarea.value = text;
	textarea.setAttribute('readonly', '');
	textarea.style.position = 'fixed';
	textarea.style.opacity = '0';
	document.body.appendChild(textarea);
	textarea.select();
	try {
		if (document.execCommand('copy')) {
			s.written++;
			return true;
		}

		return false;
	}
	catch {
		// Clipboard access is blocked.
		return false;
	}
	finally {
		textarea.remove();
		focused?.focus?.({ preventScroll: true });
	}
}

function stopWaiting(s) {
	if (s.waiting) {
		window.removeEventListener('focus', s.waiting.listener);
		s.waiting = null;
	}
}

function waitForFocus(s, copy) {
	// A refused write gets here after a promise, by which time a newer copy may have been made.
	if (copy < s.newestCopy) {
		return;
	}

	stopWaiting(s);
	const writtenBefore = s.written;
	const listener = () => {
		s.waiting = null;
		if (s.written !== writtenBefore) {
			return;
		}

		navigator.clipboard.writeText('').catch(() => {
			// Still refused (no permission at all); nothing more a page can do.
		});
	};
	s.waiting = { copy, listener };
	window.addEventListener('focus', listener, { once: true });
}

/**
 * Clears the clipboard for copy number copy. write is this page's number for that copy's write, or null when another page
 * made it.
 * @returns {Promise<boolean>} False while the clear waits for focus, so .NET knows the secret is still there and can clear
 *   it another way if the app goes away first; true once there is nothing of this copy left to clear.
 */
export async function clearClipboard(copy, write) {
	const s = state();
	if (copy < s.newestCopy) {
		return true;
	}

	s.newestCopy = copy;
	stopWaiting(s);

	// Writes still under way land first, so the check below sees the clipboard's real order, and a click's write still
	// waiting for its text is never wiped by a clear that started after it.
	await settle(s);
	if (copy < s.newestCopy || (typeof write === 'number' && s.written > write)) {
		return true;
	}

	if (document.hasFocus()) {
		try {
			await navigator.clipboard.writeText('');
			return true;
		}
		catch {
			// Refused after all; the focus is the next chance.
		}
	}

	waitForFocus(s, copy);
	return false;
}

export function forgetClearsBefore(copy) {
	const s = state();
	s.newestCopy = Math.max(s.newestCopy, copy);
	if (s.waiting && s.waiting.copy < copy) {
		stopWaiting(s);
	}
}
