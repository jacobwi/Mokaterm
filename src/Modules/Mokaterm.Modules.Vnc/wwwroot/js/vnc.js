// One noVNC RFB instance per VncSessionView, fed by .NET through VncInterop.
// noVNC 1.7.0 (@novnc/novnc, MPL-2.0), vendored in ../lib/novnc with its core and pako directories unchanged but for
// two patches marked "Mokaterm patch". Its files already end in .js, which matters because the MAUI WebView serves .mjs
// as application/octet-stream and the browser refuses to import that as a module.
//
// Apply the patches again after updating noVNC; search for "Mokaterm patch". Both stop a hostile server from making the
// page allocate a size it names before any data backs it: core/rfb.js drops extended clipboard data past 16 MiB in one
// message (extendedClipboardMaxBytes), and core/inflator.js refuses to inflate more than 256 MiB at once
// (maxInflateBytes).
//
// The RFB object thinks it talks to a WebSocket. That "socket" is the channel below: .NET has already done the real
// RFB handshake, so the page gets a synthetic one (version, security None, security result, the server's ServerInit)
// and from then on the bytes are the session itself, relayed unchanged in both directions.
import RFB from '../lib/novnc/core/rfb.js';
import KeyTable from '../lib/novnc/core/input/keysym.js';
import { byteQueue, createInstances, drain, invoke, listen } from '../../Mokaterm.UI.Common/js/interop.js';

// Messages to .NET count against SignalR's receive limit on Blazor Server (32 KB by default). Clipboard text from
// the page can be much larger than that, so outgoing bytes are coalesced and cut to half the limit.
const MaxOutgoingChunk = 16 * 1024;

const WebSocketOpen = 1;
const WebSocketClosed = 3;

// Key combinations the browser or the operating system eats before a session can see them.
const Combos = {
	'ctrl-alt-del': [[KeyTable.XK_Control_L, 'ControlLeft'], [KeyTable.XK_Alt_L, 'AltLeft'], [KeyTable.XK_Delete, 'Delete']],
	'ctrl-esc': [[KeyTable.XK_Control_L, 'ControlLeft'], [KeyTable.XK_Escape, 'Escape']],
	'alt-tab': [[KeyTable.XK_Alt_L, 'AltLeft'], [KeyTable.XK_Tab, 'Tab']],
	'alt-f4': [[KeyTable.XK_Alt_L, 'AltLeft'], [KeyTable.XK_F4, 'F4']],
	'print-screen': [[KeyTable.XK_Print, 'PrintScreen']],
	'super': [[KeyTable.XK_Super_L, 'MetaLeft']],
};

const instances = createInstances();

export function create(screen, dotNet, options) {
	const state = {
		screen,
		dotNet,
		rfb: null,
		channel: null,
		canvas: null,
		observer: null,
		outgoing: byteQueue('OnData', MaxOutgoingChunk),
		remoteClipboard: '',
		clipboardLimit: options.clipboardLimit > 0 ? options.clipboardLimit : Infinity,
		scaling: options.scaling,
		viewOnly: !!options.viewOnly,
		active: !!options.active,
		failed: false,
		closed: false,
		disposed: false,
		disposables: [],
	};

	state.channel = new DotNetChannel(state);
	const rfb = new RFB(screen, state.channel, { shared: !!options.shared });
	state.rfb = rfb;
	rfb.background = options.background || 'transparent';
	rfb.qualityLevel = level(options.quality, 6);
	rfb.compressionLevel = level(options.compression, 2);
	rfb.showDotCursor = !!options.showDotCursor;
	applyInputState(state);
	applyScaling(state, options.scaling);

	state.disposables.push(
		listen(rfb, 'connect', () => invoke(state, 'OnOpened')),
		listen(rfb, 'disconnect', event => reportClosed(state, !!event.detail?.clean)),
		listen(rfb, 'desktopname', event => invoke(state, 'OnDesktopName', String(event.detail?.name ?? ''))),
		listen(rfb, 'clipboard', event => handleClipboard(state, event.detail?.text)),
		listen(document, 'fullscreenchange', () => invoke(state, 'OnFullscreen', isFullscreen(state))),
	);

	// The canvas is sized in framebuffer pixels, so its attributes are the remote screen size without reaching into
	// noVNC's private fields.
	state.canvas = screen.querySelector('canvas');
	if (state.canvas) {
		state.observer = new MutationObserver(() => reportSize(state));
		state.observer.observe(state.canvas, { attributes: true, attributeFilter: ['width', 'height'] });
	}

	return instances.add(state);
}

// Bytes from the server. Resolving only after noVNC has parsed them is what holds the relay back while the view is busy.
export function deliver(id, data) {
	const state = instances.get(id);
	if (!state || state.disposed || state.failed) {
		return;
	}

	try {
		state.channel.receive(data);
	}
	catch (error) {
		// noVNC throws out of its receive path for data it cannot hold or parse, such as a message larger than its
		// 40 MB receive queue. Its protocol state is lost at that point, so the screen stops with an error instead
		// of freezing while the relay keeps feeding it.
		console.error('The VNC screen stopped on data from the server.', error);
		state.failed = true;
		try {
			state.rfb.disconnect();
		}
		catch {
			// Already torn down; the view is told below either way.
		}

		reportClosed(state, false);
	}
}

export function setOptions(id, changes) {
	const state = instances.get(id);
	if (!state || state.disposed) {
		return;
	}

	const { rfb } = state;
	if ('viewOnly' in changes) {
		state.viewOnly = !!changes.viewOnly;
	}

	if ('active' in changes) {
		state.active = !!changes.active;
	}

	if ('viewOnly' in changes || 'active' in changes) {
		applyInputState(state);
	}

	if ('scaling' in changes) {
		applyScaling(state, changes.scaling);
	}

	if ('quality' in changes) {
		rfb.qualityLevel = level(changes.quality, rfb.qualityLevel);
	}

	if ('compression' in changes) {
		rfb.compressionLevel = level(changes.compression, rfb.compressionLevel);
	}

	if ('showDotCursor' in changes) {
		rfb.showDotCursor = !!changes.showDotCursor;
	}

	if ('background' in changes) {
		rfb.background = changes.background;
	}
}

export function focus(id) {
	const state = instances.get(id);
	if (state && !state.disposed) {
		state.rfb.focus({ preventScroll: true });
	}
}

export function sendCombo(id, name) {
	const state = instances.get(id);
	const keys = Combos[name];
	if (!state || state.disposed || !keys || state.rfb.viewOnly) {
		return;
	}

	for (const [keysym, code] of keys) {
		state.rfb.sendKey(keysym, code, true);
	}

	for (let i = keys.length - 1; i >= 0; i--) {
		state.rfb.sendKey(keys[i][0], keys[i][1], false);
	}
}

/** Pastes text from this machine into the session. */
export function sendClipboard(id, text) {
	const state = instances.get(id);
	if (state && !state.disposed && typeof text === 'string' && text.length > 0) {
		state.rfb.clipboardPasteFrom(text);
		return true;
	}

	return false;
}

/** Puts the text the session last copied on this machine's clipboard. Returns its length, or -1 when it failed. */
export async function receiveClipboard(id) {
	const state = instances.get(id);
	const text = state?.remoteClipboard ?? '';
	if (!text) {
		return 0;
	}

	try {
		await navigator.clipboard.writeText(text);
		return text.length;
	}
	catch {
		// navigator.clipboard only exists in secure contexts, and a web host served over plain HTTP is not one.
		return copyWithCommand(text) ? text.length : -1;
	}
}

/**
 * The screen as a PNG. .NET asks for a stream, so Blazor wraps this blob itself; wrapping it here would be wrapped
 * twice and rejected. An empty array means there was nothing to capture.
 */
export async function screenshot(id) {
	const state = instances.get(id);
	const canvas = state && !state.disposed ? state.canvas ?? state.screen.querySelector('canvas') : null;
	const picture = canvas ? await new Promise(resolve => canvas.toBlob(resolve, 'image/png')) : null;
	return picture ?? new Uint8Array();
}

export function toggleFullscreen(id, root) {
	const state = instances.get(id);
	if (!state || state.disposed) {
		return Promise.resolve(false);
	}

	if (isFullscreen(state)) {
		return document.exitFullscreen().then(() => false, () => false);
	}

	const target = root || state.screen;
	if (!target?.requestFullscreen) {
		return Promise.resolve(false);
	}

	return target.requestFullscreen().then(() => true, () => false);
}

export function dispose(id) {
	const state = instances.release(id);
	if (!state) {
		return;
	}

	state.outgoing.drop();
	state.observer?.disconnect();
	try {
		state.rfb?.disconnect();
	}
	catch {
		// Already gone; the elements it created are removed with the component's own DOM anyway.
	}
}

//#region Channel

// What noVNC's Websock accepts in place of a WebSocket: it checks for these exact members, so send, close and the
// four handlers must stay.
class DotNetChannel {
	constructor(state) {
		this.binaryType = 'arraybuffer';
		this.protocol = '';
		this.readyState = WebSocketOpen;
		this.onopen = null;
		this.onmessage = null;
		this.onclose = null;
		this.onerror = null;
		this.state = state;
	}

	send(data) {
		const { state } = this;
		if (state.disposed) {
			return;
		}

		// noVNC hands out a view over its own send buffer and reuses it right after.
		state.outgoing.push(new Uint8Array(data));
		drain(state, state.outgoing);
	}

	close() {
		if (this.readyState !== WebSocketClosed) {
			this.readyState = WebSocketClosed;
			this.onclose?.({ code: 1000, reason: '' });
		}
	}

	receive(data) {
		if (this.readyState === WebSocketOpen) {
			this.onmessage?.({ data });
		}
	}
}

//#endregion

//#region Helpers

// An inactive view takes no input, and it must not leave keys pressed on the remote side either. noVNC does let go
// of every held key when view only turns on, but only after it has started refusing input, so those key ups never
// leave the page: they are sent first, while input still flows.
function applyInputState(state) {
	const { rfb } = state;
	const viewOnly = state.viewOnly || !state.active;
	if (viewOnly && !rfb.viewOnly) {
		// _keyboard is noVNC's own Keyboard (core/input/keyboard.js, vendored at 1.7.0), which has no public way to
		// do this.
		const keyboard = rfb._keyboard;
		if (keyboard && typeof keyboard._allKeysUp === 'function') {
			keyboard._allKeysUp();
		}
	}

	rfb.viewOnly = viewOnly;
}

function applyScaling(state, scaling) {
	const { rfb } = state;
	state.scaling = scaling;

	// Clipping keeps the canvas inside the view; scaling wins over it in noVNC when both are on.
	rfb.clipViewport = true;
	rfb.scaleViewport = scaling === 'fit';
	rfb.resizeSession = scaling === 'remote';
}

// noVNC reports its own disconnect while this module is still failing the screen, so the first report wins.
function reportClosed(state, clean) {
	if (!state.closed) {
		state.closed = true;
		invoke(state, 'OnClosed', clean && !state.failed);
	}
}

function handleClipboard(state, text) {
	const value = typeof text === 'string' ? text : '';
	if (value.length > state.clipboardLimit) {
		// The same limit as for text sent to the session. The copy it replaces is stale either way.
		state.remoteClipboard = '';
		invoke(state, 'OnClipboard', 0);
		invoke(state, 'OnClipboardRefused', value.length);
		return;
	}

	state.remoteClipboard = value;
	invoke(state, 'OnClipboard', value.length);
}

function reportSize(state) {
	if (state.canvas) {
		invoke(state, 'OnScreenSize', state.canvas.width, state.canvas.height);
	}
}

function isFullscreen(state) {
	return document.fullscreenElement !== null && document.fullscreenElement.contains(state.screen);
}

function copyWithCommand(text) {
	const focused = document.activeElement;
	const textarea = document.createElement('textarea');
	textarea.value = text;
	textarea.setAttribute('readonly', '');
	textarea.style.position = 'fixed';
	textarea.style.opacity = '0';
	document.body.appendChild(textarea);
	textarea.select();
	try {
		return document.execCommand('copy');
	}
	catch {
		return false;
	}
	finally {
		textarea.remove();
		focused?.focus?.({ preventScroll: true });
	}
}

function level(value, fallback) {
	const number = Number(value);
	return Number.isInteger(number) && number >= 0 && number <= 9 ? number : fallback;
}

//#endregion
