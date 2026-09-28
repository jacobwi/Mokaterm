// One RDP screen per RdpSessionView, painted by .NET through RdpInterop.
//
// Nothing about RDP is implemented here. .NET owns the connection, decodes the desktop and sends changed regions;
// this module draws them on a canvas and reports what the user does, in the compact shape RdpInputEvent reads.
import { createInstances, drain, eventQueue, invoke, listen } from '../../Mokaterm.UI.Common/js/interop.js';

// Must match RdpFrameEncoder: format, x, y, width, height and one field of padding, all little endian, which puts
// the pixels that follow on a four byte boundary.
const HeaderLength = 12;
const RawFormat = 0;

// Matches RdpInputKind.
const Kind = {
	mouseMove: 0,
	mouseDown: 1,
	mouseUp: 2,
	wheel: 3,
	keyDown: 4,
	keyUp: 5,
	releaseKeys: 6,
};

// RDP counts wheel movement in notches of 120.
const WheelNotch = 120;
const PixelsPerNotch = 100;
const LinesPerNotch = 3;

// Dragging the window or a splitter reports a new size many times a second, and every request makes the server
// rebuild its session at that size, so only the size the view settles on is asked for.
const ResizeSettleMs = 300;

// A backlog that built up while .NET was busy goes out in several calls, each well inside SignalR's receive limit
// on Blazor Server (32 KB by default).
const MaxEventsPerCall = 256;

const instances = createInstances();

export function create(screen, dotNet, options) {
	const state = {
		screen,
		dotNet,
		canvas: null,
		context: null,
		width: Math.max(1, options.width | 0),
		height: Math.max(1, options.height | 0),
		scaling: options.scaling,
		viewOnly: !!options.viewOnly,
		active: !!options.active,
		releaseKeys: !!options.releaseKeysWhenInactive,
		lastX: -1,
		lastY: -1,
		queue: eventQueue('OnInput', MaxEventsPerCall),
		pendingMove: null,
		moveScheduled: 0,
		resizeTimer: 0,
		wheelX: 0,
		wheelY: 0,
		cursorUrl: null,
		cursor: 'default',
		hidden: false,
		observer: null,
		disposables: [],
		disposed: false,
	};

	screen.style.background = options.background || 'transparent';
	buildCanvas(state);
	listenForInput(state);

	state.observer = new ResizeObserver(() => layout(state, true));
	state.observer.observe(screen);
	layout(state, false);

	return instances.add(state);
}

/** Paints one region. Resolving only after it is drawn is what holds the session back while the view is busy. */
export async function frame(id, bytes) {
	const state = instances.get(id);
	if (!state || state.disposed || bytes.length < HeaderLength) {
		return;
	}

	const header = new DataView(bytes.buffer, bytes.byteOffset, HeaderLength);
	const format = header.getUint16(0, true);
	const x = header.getUint16(2, true);
	const y = header.getUint16(4, true);
	const width = header.getUint16(6, true);
	const height = header.getUint16(8, true);
	if (width === 0 || height === 0) {
		return;
	}

	if (format === RawFormat) {
		const pixels = new Uint8ClampedArray(bytes.buffer, bytes.byteOffset + HeaderLength, width * height * 4);
		state.context.putImageData(new ImageData(pixels, width, height), x, y);
		return;
	}

	const picture = new Blob([bytes.subarray(HeaderLength)], { type: 'image/png' });
	const bitmap = await createImageBitmap(picture);
	try {
		if (!state.disposed) {
			state.context.drawImage(bitmap, x, y);
		}
	}
	finally {
		bitmap.close();
	}
}

export function pointerImage(id, png, hotspotX, hotspotY) {
	const state = instances.get(id);
	if (!state || state.disposed) {
		return;
	}

	releaseCursor(state);
	state.cursorUrl = URL.createObjectURL(new Blob([png], { type: 'image/png' }));
	state.cursor = `url("${state.cursorUrl}") ${hotspotX} ${hotspotY}, default`;
	applyCursor(state);
}

export function pointerStyle(id, hidden) {
	const state = instances.get(id);
	if (!state || state.disposed) {
		return;
	}

	releaseCursor(state);
	state.hidden = !!hidden;
	state.cursor = 'default';
	applyCursor(state);
}

/** The server moved the pointer itself. The page cannot move the real one, so it only stops guessing. */
export function pointerMoved(id, x, y) {
	const state = instances.get(id);
	if (state && !state.disposed) {
		state.pendingMove = null;
		state.lastX = x;
		state.lastY = y;
	}
}

export function resize(id, width, height) {
	const state = instances.get(id);
	if (!state || state.disposed) {
		return;
	}

	state.width = Math.max(1, width | 0);
	state.height = Math.max(1, height | 0);
	buildCanvas(state);
	layout(state, false);
}

export function setOptions(id, changes) {
	const state = instances.get(id);
	if (!state || state.disposed) {
		return;
	}

	if ('viewOnly' in changes) {
		state.viewOnly = !!changes.viewOnly;
	}

	if ('releaseKeysWhenInactive' in changes) {
		state.releaseKeys = !!changes.releaseKeysWhenInactive;
	}

	if ('active' in changes) {
		state.active = !!changes.active;

		// A tab switched away from mid keystroke would leave that key down on the server. Turning view only on
		// releases keys on the .NET side instead, so only the hidden case is handled here.
		if (!state.active && state.releaseKeys) {
			push(state, { kind: Kind.releaseKeys, a: 0, b: 0, code: null });
		}
	}

	if ('scaling' in changes) {
		state.scaling = changes.scaling;
		layout(state, true);
	}

	if ('background' in changes) {
		state.screen.style.background = changes.background;
	}
}

export function focus(id) {
	const state = instances.get(id);
	if (state && !state.disposed) {
		state.canvas.focus({ preventScroll: true });
	}
}

/**
 * The screen as a PNG. .NET asks for a stream, so Blazor wraps this blob itself; wrapping it here would be wrapped
 * twice and rejected. An empty array means there was nothing to capture.
 */
export async function screenshot(id) {
	const state = instances.get(id);
	const canvas = state && !state.disposed ? state.canvas : null;
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

	state.queue.drop();
	state.observer?.disconnect();
	if (state.moveScheduled) {
		cancelAnimationFrame(state.moveScheduled);
		state.moveScheduled = 0;
	}

	clearTimeout(state.resizeTimer);
	state.resizeTimer = 0;
	releaseCursor(state);
	state.canvas?.remove();
}

//#region Canvas

function buildCanvas(state) {
	const canvas = document.createElement('canvas');
	canvas.width = state.width;
	canvas.height = state.height;
	canvas.className = 'mt-rdp-canvas';
	canvas.tabIndex = 0;

	// An opaque canvas forces every pixel to full alpha, so a codec that leaves the fourth byte alone cannot
	// punch holes in the desktop, and the browser skips one blending pass per frame.
	const context = canvas.getContext('2d', { alpha: false });
	context.imageSmoothingEnabled = false;

	state.canvas?.remove();
	state.canvas = canvas;
	state.context = context;
	state.screen.appendChild(canvas);
	applyCursor(state);
}

function layout(state, report) {
	const { screen, canvas } = state;
	const available = screen.getBoundingClientRect();
	if (available.width === 0 || available.height === 0) {
		return;
	}

	if (state.scaling === 'fit') {
		const scale = Math.min(available.width / state.width, available.height / state.height);
		canvas.style.width = `${Math.max(1, Math.floor(state.width * scale))}px`;
		canvas.style.height = `${Math.max(1, Math.floor(state.height * scale))}px`;
		screen.style.overflow = 'hidden';
	}
	else {
		canvas.style.width = `${state.width}px`;
		canvas.style.height = `${state.height}px`;
		screen.style.overflow = 'auto';
	}

	if (report && state.scaling === 'remote') {
		// Only the server can change the desktop size, so the view asks .NET and waits for the resize to come back.
		requestServerSize(state, Math.round(available.width), Math.round(available.height));
	}
}

function requestServerSize(state, width, height) {
	clearTimeout(state.resizeTimer);
	state.resizeTimer = setTimeout(() => {
		state.resizeTimer = 0;
		invoke(state, 'OnViewSize', width, height);
	}, ResizeSettleMs);
}

function applyCursor(state) {
	if (state.canvas) {
		state.canvas.style.cursor = state.hidden ? 'none' : state.cursor;
	}
}

function releaseCursor(state) {
	if (state.cursorUrl) {
		URL.revokeObjectURL(state.cursorUrl);
		state.cursorUrl = null;
	}
}

function isFullscreen(state) {
	return document.fullscreenElement !== null && document.fullscreenElement.contains(state.screen);
}

//#endregion

//#region Input

function listenForInput(state) {
	const { canvas } = state;
	state.disposables.push(
		listen(canvas, 'mousemove', event => onMouseMove(state, event)),
		listen(canvas, 'mousedown', event => onMouseButton(state, event, true)),
		listen(canvas, 'mouseup', event => onMouseButton(state, event, false)),
		listen(canvas, 'wheel', event => onWheel(state, event), { passive: false }),
		listen(canvas, 'contextmenu', event => event.preventDefault()),
		listen(canvas, 'keydown', event => onKey(state, event, true)),
		listen(canvas, 'keyup', event => onKey(state, event, false)),
		listen(canvas, 'blur', () => releaseKeys(state)),
		listen(window, 'blur', () => releaseKeys(state)),
		listen(document, 'fullscreenchange', () => invoke(state, 'OnFullscreen', isFullscreen(state))),
	);
}

function takesInput(state) {
	return !state.disposed && state.active && !state.viewOnly;
}

function remotePoint(state, event) {
	const rect = state.canvas.getBoundingClientRect();
	if (rect.width === 0 || rect.height === 0) {
		return null;
	}

	const x = Math.round((event.clientX - rect.left) * (state.width / rect.width));
	const y = Math.round((event.clientY - rect.top) * (state.height / rect.height));
	return {
		x: Math.min(state.width - 1, Math.max(0, x)),
		y: Math.min(state.height - 1, Math.max(0, y)),
	};
}

// Moves are collapsed to one per animation frame: a pointer crossing the screen fires far more events than a
// session needs, and each one would be a message.
function onMouseMove(state, event) {
	if (!takesInput(state)) {
		return;
	}

	const point = remotePoint(state, event);
	if (!point) {
		return;
	}

	state.pendingMove = point;
	if (!state.moveScheduled) {
		state.moveScheduled = requestAnimationFrame(() => {
			state.moveScheduled = 0;
			flushMove(state);
			drain(state, state.queue);
		});
	}
}

function flushMove(state) {
	const move = state.pendingMove;
	state.pendingMove = null;
	if (!move) {
		return;
	}

	state.lastX = move.x;
	state.lastY = move.y;

	// Moves queued behind a slow call collapse into the last one: only where the pointer ended up matters, and a
	// replayed path would drag whatever the button is holding along every point of it.
	const last = state.queue.last();
	if (last && last.kind === Kind.mouseMove) {
		last.a = move.x;
		last.b = move.y;
	}
	else {
		state.queue.push({ kind: Kind.mouseMove, a: move.x, b: move.y, code: null });
	}
}

function onMouseButton(state, event, down) {
	if (!takesInput(state)) {
		return;
	}

	event.preventDefault();
	if (down) {
		state.canvas.focus({ preventScroll: true });
	}

	// The button lands where the pointer already is, so any move still waiting goes first.
	const point = remotePoint(state, event);
	if (point && (point.x !== state.lastX || point.y !== state.lastY)) {
		state.pendingMove = point;
	}

	flushMove(state);
	push(state, { kind: down ? Kind.mouseDown : Kind.mouseUp, a: event.button, b: 0, code: null });
}

function onWheel(state, event) {
	if (!takesInput(state)) {
		return;
	}

	event.preventDefault();
	const scale = event.deltaMode === 1 ? LinesPerNotch : event.deltaMode === 2 ? 1 : PixelsPerNotch;
	state.wheelY += (event.deltaY / scale) * WheelNotch;
	state.wheelX += (event.deltaX / scale) * WheelNotch;

	const vertical = Math.trunc(state.wheelY);
	const horizontal = Math.trunc(state.wheelX);
	state.wheelY -= vertical;
	state.wheelX -= horizontal;
	flushMove(state);
	if (vertical) {
		state.queue.push({ kind: Kind.wheel, a: 1, b: vertical, code: null });
	}

	if (horizontal) {
		state.queue.push({ kind: Kind.wheel, a: 0, b: horizontal, code: null });
	}

	drain(state, state.queue);
}

function onKey(state, event, down) {
	if (!takesInput(state)) {
		return;
	}

	// A session that has the keyboard takes every key, F5 and Ctrl+W included, or the browser acts instead.
	event.preventDefault();
	event.stopPropagation();
	if (event.repeat && !down) {
		return;
	}

	push(state, { kind: down ? Kind.keyDown : Kind.keyUp, a: 0, b: 0, code: event.code });
}

function releaseKeys(state) {
	if (!state.disposed) {
		push(state, { kind: Kind.releaseKeys, a: 0, b: 0, code: null });
	}
}

function push(state, event) {
	state.queue.push(event);
	drain(state, state.queue);
}

//#endregion
