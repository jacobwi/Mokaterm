// One xterm.js instance per TerminalView, driven from .NET through TerminalInterop.
// xterm.js 6.0.0 with addon-fit 0.11.0, addon-search 0.16.0, addon-unicode11 0.9.0, addon-web-links 0.12.0 and
// addon-webgl 0.19.0: the npm ESM builds, vendored with a .js extension because the MAUI WebView serves .mjs as
// application/octet-stream, which browsers refuse to run as a module.
import { Terminal } from '../lib/xterm/xterm.js';
import { FitAddon } from '../lib/xterm/addon-fit.js';
import { SearchAddon } from '../lib/xterm/addon-search.js';
import { Unicode11Addon } from '../lib/xterm/addon-unicode11.js';
import { WebLinksAddon } from '../lib/xterm/addon-web-links.js';
import { createInstances, disposeQuietly, invoke, listen } from '../../Mokaterm.UI.Common/js/interop.js';

// JS-to-.NET messages count against SignalR's receive limit on Blazor Server (32 KB by default). A chunk of input
// made only of control characters is 6 bytes per character as JSON, so 4096 characters stays below it.
const MaxInputChunk = 4096;
const MaxTitleLength = 1024;
const ResizeDebounceMs = 60;
const MaxFitRetries = 10;
const FontLoadTimeoutMs = 1500;
const BellThrottleMs = 200;
const BellFlashMs = 160;
const BellClass = 'mt-terminal-bell';
const PastePreviewLines = 5;
const PastePreviewLineLength = 120;
// C0 controls except tab, line feed and carriage return, then DEL and the C1 controls: the characters Windows Terminal
// and xterm leave out of a paste.
const PasteControlCharacters = /[\x00-\x08\x0b\x0c\x0e-\x1f\x7f-\x9f]/g;
// Bidirectional controls change the order the confirmation dialog draws a line in, not the order the shell reads it, so
// the preview leaves them out.
const BidiControls = /[\u061c\u200e\u200f\u202a-\u202e\u2066-\u2069]/g;
const HexColor = /^#(?:[0-9a-f]{3,4}|[0-9a-f]{6}|[0-9a-f]{8})$/i;
// xterm.js draws a 14px scrollbar by default, heavy next to the thin scrollbars around it. The fit addon reserves the
// same width, and TerminalView.razor.css keeps the find bar clear of it.
const ScrollbarWidth = 10;
const MinFontSize = 6;
const MaxFontSize = 72;
// A mouse wheel notch is about 100; a trackpad pinch sends many small deltas that add up to one step.
const WheelZoomStep = 50;
const BadgeMs = 900;
const BadgeVisibleClass = 'mt-terminal-badge--visible';
// OSC 52 payloads are base64, so this is about 750 KB of text.
const MaxClipboardSequence = 1024 * 1024;
// A hovered row is reported to .NET for the save-command buttons; anything longer is output, not a command.
const MaxHoverTextLength = 1024;
const MinHoverHeight = 16;
// user@host:path$ in its usual shapes: Debian's "root@box:~#", Red Hat's "[root@box ~]#", zsh's "user@box ~ %", with an
// optional "(venv) " in front. Groups: 1 what comes before the user, 2 user, 3 machine, 4 separator, 5 folder.
const PromptPattern = /^((?:\([^)]{1,60}\)\s*)?\[?)([A-Za-z_][A-Za-z0-9_.-]{0,31})@([A-Za-z0-9][A-Za-z0-9._-]{0,62})(?:([: ])([^\s$#%>\]]{1,200}))?\]?\s?[$#%>](?=\s|$)/;
// A prompt is drawn at the cursor, so a pass never needs to look further back than the output of one busy frame.
const PromptScanLimit = 200;
const PromptRepaintLimit = 2000;
// xterm.js runs a listener of every marker for each line a full scrollback drops, and keeps a hidden element for every
// decoration that has been on screen, so only the newest prompts keep their colors.
const MaxPromptMarks = 200;
const IsMac = /Mac|iPhone|iPad|iPod/.test(navigator.userAgentData?.platform ?? navigator.platform ?? '');
const LinkHint = IsMac ? 'Cmd+click to open' : 'Ctrl+click to open';
const MaxLinkLabelLength = 300;

const instances = createInstances();
let stylesheet;
let webglAddon;
let audioContext;

export async function create(host, screen, dotNet, options, cols, rows) {
	await ensureStylesheet();
	await loadFont(options.fontFamily, options.fontSize);
	const WebglAddon = options.useWebGl ? await loadWebglAddon() : null;

	// The address goes in the hint: an OSC 8 link shows whatever text the server chose, which need not be where it leads.
	const linkHover = {
		hover: (event, uri) => { screen.title = `${linkLabel(uri)}\n${LinkHint}`; },
		leave: () => { screen.removeAttribute('title'); },
	};
	const terminalOptions = {
		allowProposedApi: true,
		cursorBlink: options.cursorBlink,
		cursorStyle: options.cursorStyle,
		fontFamily: options.fontFamily,
		fontSize: options.fontSize,
		letterSpacing: options.letterSpacing,
		lineHeight: options.lineHeight,
		scrollback: options.scrollback,
		minimumContrastRatio: options.minimumContrastRatio,
		drawBoldTextInBrightColors: options.drawBoldTextInBrightColors,
		theme: xtermTheme(options.theme),
		overviewRuler: { width: ScrollbarWidth },
		// OSC 8 hyperlinks; without a handler xterm.js asks through window.confirm, which a WebView may not show.
		linkHandler: { activate: (event, uri) => openLink(event, uri), ...linkHover },
	};
	// Start at the size the remote side already uses, so output that arrives while the view is hidden lays out right.
	// xterm.js rejects an undefined cols or rows, so they are only set when known.
	if (cols > 0 && rows > 0) {
		terminalOptions.cols = cols;
		terminalOptions.rows = rows;
	}

	const term = new Terminal(terminalOptions);
	const state = {
		term,
		host,
		screen,
		dotNet,
		fit: new FitAddon(),
		search: new SearchAddon(),
		webgl: null,
		webglLoss: null,
		observer: null,
		bell: options.bell,
		copyOnSelect: options.copyOnSelect,
		rightClickAction: options.rightClickAction,
		confirmMultiLinePaste: options.confirmMultiLinePaste,
		allowClipboardWrite: options.allowClipboardWrite,
		baseFontSize: options.fontSize,
		zoom: 0,
		wheelZoom: 0,
		atBottom: true,
		badge: host.querySelector('.mt-terminal-badge'),
		badgeTimer: 0,
		decorations: searchDecorations(options.theme),
		inputQueue: [],
		inputBusy: false,
		pendingWrites: new Set(),
		reportedCols: 0,
		reportedRows: 0,
		resizeTimer: 0,
		fitFrame: 0,
		fitRetries: 0,
		bellTimer: 0,
		lastBell: 0,
		selectionSnapshot: null,
		hoverRow: null,
		hoverText: '',
		hoverTop: -1,
		pointerDown: false,
		searchResult: { index: -1, count: 0 },
		searchActive: false,
		searchOptionsKey: '',
		inSearch: false,
		optionsQueue: Promise.resolve(),
		promptColors: options.promptColors ?? 'plain',
		promptHostColor: options.promptHostColor ?? null,
		promptMarks: [],
		promptAnchor: null,
		promptFrame: 0,
		disposables: [],
		disposed: false,
	};
	state.sendResize = latestSender(size => dotNet.invokeMethodAsync('OnResize', size.cols, size.rows, size.width, size.height));
	state.sendTitle = latestSender(title => dotNet.invokeMethodAsync('OnTitleChanged', title));
	state.sendSearchResults = latestSender(result => dotNet.invokeMethodAsync('OnSearchResults', result.index, result.count));
	state.sendHover = latestSender(hover => dotNet.invokeMethodAsync('OnRowHover', hover.text, hover.top, hover.height));

	try {
		term.loadAddon(state.fit);
		term.loadAddon(state.search);
		term.loadAddon(new Unicode11Addon());
		term.unicode.activeVersion = '11';
		term.loadAddon(new WebLinksAddon((event, uri) => openLink(event, uri), linkHover));
		term.open(screen);
		if (WebglAddon) {
			enableWebgl(state, WebglAddon);
		}

		state.disposables.push(
			term.onData(data => queueInput(state, 'text', data)),
			term.onBinary(data => queueInput(state, 'binary', data)),
			term.onTitleChange(title => state.sendTitle(title.slice(0, safeEnd(title, MaxTitleLength)))),
			term.onBell(() => ringBell(state)),
			state.search.onDidChangeResults(result => handleSearchResults(state, result)),
			term.onScroll(() => updateScrollState(state)),
			term.buffer.onBufferChange(() => {
				updateScrollState(state);
				schedulePromptScan(state);
			}),
			term.onWriteParsed(() => schedulePromptScan(state)),
			term.onRender(range => refreshHover(state, range)),
			term.parser.registerOscHandler(52, data => handleClipboardSequence(state, data)),
			listen(screen, 'wheel', event => handleWheel(state, event), true),
			listen(screen, 'paste', event => handlePasteEvent(state, event), true),
			listen(screen, 'contextmenu', event => handleContextMenu(state, event)),
			listen(screen, 'mousedown', event => handleMouseDown(state, event)),
			// The host, not the screen: moving onto the buttons Blazor draws over a row must not count as leaving it.
			listen(host, 'mousemove', event => updateHover(state, event)),
			listen(host, 'mouseleave', () => clearHover(state)),
		);
		term.attachCustomKeyEventHandler(event => handleKey(state, event));
	}
	catch (error) {
		for (const disposable of state.disposables) {
			disposeQuietly(disposable);
		}

		disposeQuietly(term);
		throw error;
	}

	const id = instances.add(state);
	const size = fitNow(state);
	state.observer = new ResizeObserver(() => scheduleFit(state));
	state.observer.observe(screen);
	return { id, size };
}

// Resolves once xterm.js has parsed the bytes, so awaiting it in .NET holds the pump back while the view is busy.
export function write(id, data) {
	const state = instances.get(id);
	if (!state || data.length === 0) {
		return Promise.resolve();
	}

	return new Promise(resolve => {
		const done = () => {
			state.pendingWrites.delete(done);
			resolve();
		};
		state.pendingWrites.add(done);
		state.term.write(data, done);
	});
}

export function fit(id) {
	const state = instances.get(id);
	if (state) {
		clearTimeout(state.resizeTimer);
		fitAndReport(state);
	}
}

export function focus(id) {
	instances.get(id)?.term.focus();
}

export function clear(id) {
	const state = instances.get(id);
	if (state) {
		state.term.clear();
		updateScrollState(state);
		repaintPrompts(state);
	}
}

export function reset(id) {
	const state = instances.get(id);
	if (state) {
		state.inputQueue.length = 0;
		state.term.reset();
		updateScrollState(state);
		repaintPrompts(state);
	}
}

export function scrollToBottom(id) {
	instances.get(id)?.term.scrollToBottom();
}

export function selectAll(id) {
	instances.get(id)?.term.selectAll();
}

// Returns the selection in slices so a large one never exceeds the Blazor Server message limit in one reply.
export function getSelection(id, offset, maxLength) {
	const state = instances.get(id);
	if (!state) {
		return { text: '', next: -1 };
	}

	if (offset === 0 || state.selectionSnapshot === null) {
		state.selectionSnapshot = state.term.getSelection();
	}

	const snapshot = state.selectionSnapshot;
	const end = safeEnd(snapshot, Math.min(snapshot.length, offset + Math.max(2, maxLength)));
	const text = snapshot.slice(offset, end);
	if (end >= snapshot.length) {
		state.selectionSnapshot = null;
		return { text, next: -1 };
	}

	return { text, next: end };
}

// The clipboard is read here, not in .NET: on Blazor Server it would cross the circuit twice, and a reply past the
// message size limit ends the connection.
export async function pasteFromClipboard(id) {
	const state = instances.get(id);
	if (!state || state.disposed) {
		return;
	}

	const text = await navigator.clipboard.readText();
	await pasteText(state, text);
}

// Applies only the options that changed; .NET sends nothing else. Font loading makes a change asynchronous, so changes
// are chained: quick successive ones (a font size slider) must not land out of order.
export function setOptions(id, changes) {
	const state = instances.get(id);
	if (!state) {
		return Promise.resolve();
	}

	const applied = state.optionsQueue.then(() => applyOptions(state, changes));
	state.optionsQueue = applied.catch(() => { });
	return applied;
}

async function applyOptions(state, changes) {
	if (state.disposed) {
		return;
	}

	const { term } = state;
	if ('fontFamily' in changes || 'fontSize' in changes) {
		await loadFont(changes.fontFamily ?? term.options.fontFamily, changes.fontSize ?? term.options.fontSize);
		if (state.disposed) {
			return;
		}
	}

	let metricsChanged = false;
	if ('fontSize' in changes) {
		// The view's zoom stays on top of the new base size.
		state.baseFontSize = changes.fontSize;
		term.options.fontSize = zoomedFontSize(state);
		metricsChanged = true;
	}

	for (const key of ['fontFamily', 'lineHeight', 'letterSpacing']) {
		if (key in changes) {
			term.options[key] = changes[key];
			metricsChanged = true;
		}
	}

	for (const key of ['cursorStyle', 'cursorBlink', 'scrollback', 'minimumContrastRatio', 'drawBoldTextInBrightColors']) {
		if (key in changes) {
			term.options[key] = changes[key];
		}
	}

	if ('theme' in changes) {
		term.options.theme = xtermTheme(changes.theme);
		state.decorations = searchDecorations(changes.theme);
	}

	if ('promptColors' in changes) {
		state.promptColors = changes.promptColors ?? 'plain';
	}

	if ('promptHostColor' in changes) {
		state.promptHostColor = changes.promptHostColor ?? null;
	}

	// The prompt colors come from the theme and the machine, so any of the three repaints what is already on screen.
	if ('theme' in changes || 'promptColors' in changes || 'promptHostColor' in changes) {
		repaintPrompts(state);
	}

	for (const key of ['bell', 'copyOnSelect', 'rightClickAction', 'confirmMultiLinePaste', 'allowClipboardWrite']) {
		if (key in changes) {
			state[key] = changes[key];
		}
	}

	if ('useWebGl' in changes) {
		if (!changes.useWebGl) {
			disableWebgl(state);
		}
		else if (!state.webgl) {
			const WebglAddon = await loadWebglAddon();
			if (WebglAddon && !state.disposed && !state.webgl) {
				enableWebgl(state, WebglAddon);
			}
		}
	}

	if (metricsChanged && !state.disposed) {
		fitAndReport(state);
		// The renderer can settle its cell size a frame later; the debounced fit catches that.
		scheduleFit(state);
	}
}

export function findNext(id, term, incremental, flags) {
	const state = instances.get(id);
	return state
		? runSearch(state, term, flags, () => state.search.findNext(term, searchOptions(state, incremental, flags)))
		: noSearchResult();
}

export function findPrevious(id, term, flags) {
	const state = instances.get(id);
	return state
		? runSearch(state, term, flags, () => state.search.findPrevious(term, searchOptions(state, false, flags)))
		: noSearchResult();
}

// Removes the highlights but keeps the selected match, so it can still be copied.
export function clearSearch(id) {
	const state = instances.get(id);
	if (state) {
		state.searchActive = false;
		state.search.clearDecorations();
		state.searchResult = { index: -1, count: 0 };
	}
}

export function focusFindInput(host) {
	const input = host?.querySelector('input[data-terminal-find]');
	if (input) {
		input.focus({ preventScroll: true });
		input.select();
	}
}

export function dispose(id) {
	const state = instances.release(id);
	if (!state) {
		return;
	}

	clearTimeout(state.resizeTimer);
	clearTimeout(state.bellTimer);
	clearTimeout(state.badgeTimer);
	state.badge?.classList.remove(BadgeVisibleClass);
	cancelAnimationFrame(state.fitFrame);
	cancelAnimationFrame(state.promptFrame);
	forgetPrompts(state);
	state.observer?.disconnect();
	state.inputQueue.length = 0;
	state.selectionSnapshot = null;
	state.hoverRow = null;
	state.host.classList.remove(BellClass);
	state.screen.removeAttribute('title');
	// xterm.js never calls back for writes it has not parsed when disposed, and .NET is awaiting them.
	for (const done of [...state.pendingWrites]) {
		done();
	}

	// Disposes the addons too, WebGL included.
	disposeQuietly(state.term);
	state.webgl = null;
	state.webglLoss = null;
}

//#region Input

function queueInput(state, kind, data) {
	if (state.disposed || data.length === 0) {
		return;
	}

	const queue = state.inputQueue;
	const last = queue[queue.length - 1];
	if (last && last.kind === kind) {
		last.data += data;
	}
	else {
		queue.push({ kind, data });
	}

	if (!state.inputBusy) {
		flushInput(state);
	}
}

// One call at a time: keys typed while a call is in flight join the next one, which keeps typing fast when every
// call is a SignalR round trip.
async function flushInput(state) {
	state.inputBusy = true;
	try {
		while (state.inputQueue.length > 0 && !state.disposed) {
			const segment = state.inputQueue[0];
			const end = segment.data.length <= MaxInputChunk ? segment.data.length : splitPoint(segment.data, MaxInputChunk);
			const chunk = segment.data.slice(0, end);
			segment.data = segment.data.slice(end);
			if (segment.data.length === 0) {
				state.inputQueue.shift();
			}

			if (segment.kind === 'text') {
				await state.dotNet.invokeMethodAsync('OnInput', chunk);
			}
			else {
				await state.dotNet.invokeMethodAsync('OnBinaryInput', binaryToBytes(chunk));
			}
		}
	}
	catch {
		// The .NET side is gone; input typed now has nowhere to go.
		state.inputQueue.length = 0;
	}
	finally {
		state.inputBusy = false;
	}
}

// Ends a chunk before a nearby escape character so a sequence is not split across two remote writes (applications
// with an escape timeout would read a lone Escape), and never between the halves of a surrogate pair.
function splitPoint(text, max) {
	let end = max;
	const escape = text.lastIndexOf('\x1b', end - 1);
	if (escape > 0 && end - escape < 32) {
		end = escape;
	}

	return safeEnd(text, end);
}

function safeEnd(text, end) {
	if (end >= text.length) {
		return text.length;
	}

	const code = text.charCodeAt(end - 1);
	return code >= 0xd800 && code <= 0xdbff ? end - 1 : end;
}

// onBinary delivers one byte per character (mouse reports that are not valid UTF-8).
function binaryToBytes(data) {
	const bytes = new Uint8Array(data.length);
	for (let i = 0; i < data.length; i++) {
		bytes[i] = data.charCodeAt(i) & 0xff;
	}

	return bytes;
}

//#endregion

//#region Size

function scheduleFit(state) {
	clearTimeout(state.resizeTimer);
	state.resizeTimer = setTimeout(() => fitAndReport(state), ResizeDebounceMs);
}

// A changed grid flashes its new size over the terminal, as tmux and Windows Terminal do while resizing; the first fit
// after opening does not. A label (the zoomed font size) shows even when the grid kept its size.
function fitAndReport(state, badgeLabel) {
	const resized = state.reportedCols > 0;
	const size = fitNow(state);
	if (size) {
		state.sendResize(size);
	}

	if ((size && resized) || badgeLabel) {
		const grid = `${state.term.cols} × ${state.term.rows}`;
		showBadge(state, badgeLabel ? `${badgeLabel}  ${grid}` : grid);
	}
}

function showBadge(state, text) {
	const { badge } = state;
	if (!badge || state.disposed) {
		return;
	}

	badge.textContent = text;
	badge.classList.add(BadgeVisibleClass);
	clearTimeout(state.badgeTimer);
	state.badgeTimer = setTimeout(() => badge.classList.remove(BadgeVisibleClass), BadgeMs);
}

// Zoom belongs to the view and lasts until it closes; the settings keep the base size.
function zoomBy(state, step) {
	setZoom(state, state.zoom + step);
}

function setZoom(state, zoom) {
	state.zoom = Math.min(MaxFontSize, Math.max(MinFontSize, state.baseFontSize + zoom)) - state.baseFontSize;
	const size = zoomedFontSize(state);
	if (state.term.options.fontSize !== size) {
		state.term.options.fontSize = size;
	}

	fitAndReport(state, `${size}px`);
	// The renderer can settle its cell size a frame later; the debounced fit catches that.
	scheduleFit(state);
}

function zoomedFontSize(state) {
	return Math.min(MaxFontSize, Math.max(MinFontSize, state.baseFontSize + state.zoom));
}

// Ctrl+wheel (Cmd+wheel on macOS) zooms the terminal. Left alone, the WebView would zoom the whole page instead.
function handleWheel(state, event) {
	if (!(IsMac ? event.metaKey : event.ctrlKey) || event.deltaY === 0) {
		return;
	}

	event.preventDefault();
	event.stopPropagation();
	state.wheelZoom += event.deltaY;
	if (Math.abs(state.wheelZoom) >= WheelZoomStep) {
		zoomBy(state, state.wheelZoom < 0 ? 1 : -1);
		state.wheelZoom = 0;
	}
}

// Tells .NET when the view leaves or returns to the end of the output, so it can offer a way back.
function updateScrollState(state) {
	if (state.disposed) {
		return;
	}

	const buffer = state.term.buffer.active;
	const atBottom = buffer.viewportY >= buffer.baseY;
	if (atBottom !== state.atBottom) {
		state.atBottom = atBottom;
		invoke(state, 'OnScrollStateChanged', atBottom);
	}
}

// Fits the grid to the element and returns the new size, or null when hidden or unchanged. A hidden element
// (display: none) resolves height: 100% to a bogus pixel value, so it must never be measured.
function fitNow(state) {
	const { screen, term } = state;
	if (state.disposed || !screen.isConnected || screen.clientWidth === 0 || screen.clientHeight === 0) {
		return null;
	}

	const proposed = state.fit.proposeDimensions();
	if (!proposed || Number.isNaN(proposed.cols) || Number.isNaN(proposed.rows)) {
		retryFit(state);
		return null;
	}

	state.fitRetries = 0;
	state.fit.fit();
	if (term.cols === state.reportedCols && term.rows === state.reportedRows) {
		return null;
	}

	state.reportedCols = term.cols;
	state.reportedRows = term.rows;
	const canvas = term.element?.querySelector('.xterm-screen');
	return { cols: term.cols, rows: term.rows, width: canvas?.clientWidth ?? 0, height: canvas?.clientHeight ?? 0 };
}

// Character cells can be unmeasured for a few frames after the terminal first becomes visible.
function retryFit(state) {
	if (state.fitFrame || state.fitRetries >= MaxFitRetries) {
		return;
	}

	state.fitRetries++;
	state.fitFrame = requestAnimationFrame(() => {
		state.fitFrame = 0;
		fitAndReport(state);
	});
}

//#endregion

//#region Hovered row

// Reports the row under the pointer to .NET, which decides whether it holds a command and draws the save buttons over
// it. The xterm.js DOM is never touched, and nothing is reported while an application is reading the mouse itself.
function updateHover(state, event) {
	if (state.disposed || state.pointerDown || state.term.modes.mouseTrackingMode !== 'none') {
		clearHover(state);
		return;
	}

	const metrics = rowMetrics(state);
	if (!metrics) {
		clearHover(state);
		return;
	}

	const offset = event.clientY - metrics.screenTop;
	if (offset < 0 || offset >= metrics.height * state.term.rows) {
		clearHover(state);
		return;
	}

	sendHover(state, Math.min(state.term.rows - 1, Math.floor(offset / metrics.height)), metrics);
}

// The hovered row keeps reporting while it changes, so the line being typed right now stays current.
function refreshHover(state, range) {
	if (state.hoverRow === null || state.hoverRow < range.start || state.hoverRow > range.end) {
		return;
	}

	const metrics = rowMetrics(state);
	if (metrics) {
		sendHover(state, state.hoverRow, metrics);
	}
}

function sendHover(state, row, metrics) {
	const buffer = state.term.buffer.active;
	const line = buffer.getLine(buffer.viewportY + row);
	const text = line ? line.translateToString(true).slice(0, MaxHoverTextLength) : '';
	const top = Math.round(metrics.offsetTop + row * metrics.height);
	if (state.hoverRow === row && state.hoverText === text && state.hoverTop === top) {
		return;
	}

	state.hoverRow = row;
	state.hoverText = text;
	state.hoverTop = top;
	state.sendHover({ text, top, height: Math.max(MinHoverHeight, Math.round(metrics.height)) });
}

function clearHover(state) {
	if (state.hoverRow === null) {
		return;
	}

	state.hoverRow = null;
	state.hoverText = '';
	state.hoverTop = -1;
	state.sendHover({ text: '', top: 0, height: 0 });
}

// Row height and where the grid sits inside the view. Null while the terminal has no measured rows.
function rowMetrics(state) {
	const screen = state.term.element?.querySelector('.xterm-screen');
	if (!screen || state.term.rows === 0) {
		return null;
	}

	const rect = screen.getBoundingClientRect();
	if (rect.height === 0) {
		return null;
	}

	return {
		screenTop: rect.top,
		offsetTop: rect.top - state.host.getBoundingClientRect().top,
		height: rect.height / state.term.rows,
	};
}

//#endregion

//#region Keyboard and clipboard

// Called by xterm.js for keydown, keypress and keyup. Returning false stops xterm.js from handling the key, but it
// does not cancel the browser's default action, so keydown calls preventDefault itself.
function handleKey(state, event) {
	const action = keyAction(state, event);
	if (!action) {
		return true;
	}

	if (event.type === 'keydown') {
		// Same as the keys xterm.js consumes: page-level shortcuts must not also react.
		event.preventDefault();
		event.stopPropagation();
		switch (action) {
			case 'copy':
				copySelection(state, false);
				break;
			case 'copy-and-clear':
				copySelection(state, true);
				break;
			case 'find':
				invoke(state, 'OnFindRequested');
				break;
			case 'zoom-in':
				zoomBy(state, 1);
				break;
			case 'zoom-out':
				zoomBy(state, -1);
				break;
			case 'zoom-reset':
				setZoom(state, 0);
				break;
			case 'scroll-top':
				state.term.scrollToTop();
				break;
			case 'scroll-bottom':
				state.term.scrollToBottom();
				break;
		}
	}

	return false;
}

// Ctrl+Shift+V and Shift+Insert are not listed: the browser turns them into a paste event, which handlePasteEvent
// routes through the multi-line check. Reading the clipboard that way needs no permission prompt.
function keyAction(state, event) {
	const zoom = zoomAction(event);
	if (zoom) {
		return zoom;
	}

	if (event.altKey || event.metaKey) {
		return null;
	}

	if (event.key === 'Insert') {
		return event.ctrlKey && !event.shiftKey ? 'copy' : null;
	}

	if (!event.ctrlKey) {
		return null;
	}

	const letter = keyLetter(event);
	if (event.shiftKey) {
		switch (event.key) {
			case 'Home':
				return 'scroll-top';
			case 'End':
				return 'scroll-bottom';
			default:
				return letter === 'c' ? 'copy' : letter === 'f' ? 'find' : null;
		}
	}

	// Ctrl+C copies only while something is selected; otherwise it is an interrupt for the remote side. On macOS
	// Cmd+C copies (natively, through xterm.js), so Ctrl+C always stays an interrupt there.
	return !IsMac && letter === 'c' && state.term.hasSelection() ? 'copy-and-clear' : null;
}

// Ctrl (Cmd on macOS) with = or + zooms in, with - zooms out and with 0 resets, as in Windows Terminal. Ctrl+Shift+-
// is left to the remote side: it types Ctrl+_, which Emacs and readline use for undo.
function zoomAction(event) {
	const modifier = IsMac ? event.metaKey && !event.ctrlKey : event.ctrlKey && !event.metaKey;
	if (!modifier || event.altKey) {
		return null;
	}

	switch (event.key) {
		case '=':
		case '+':
			return 'zoom-in';
		case '-':
			return event.shiftKey ? null : 'zoom-out';
		case '0':
			return event.shiftKey ? null : 'zoom-reset';
		default:
			return null;
	}
}

// OSC 52 ("c;<base64>") is how tmux, Neovim and other remote programs copy to the local clipboard. Only writes are
// honored, and only when the settings allow it: answering a query ("?") would hand the clipboard to the server.
function handleClipboardSequence(state, data) {
	if (!state.allowClipboardWrite) {
		return true;
	}

	const separator = data.indexOf(';');
	const payload = separator < 0 ? '' : data.slice(separator + 1);
	if (payload.length === 0 || payload === '?' || payload.length > MaxClipboardSequence) {
		return true;
	}

	let text;
	try {
		const bytes = Uint8Array.from(atob(payload), character => character.charCodeAt(0));
		text = new TextDecoder().decode(bytes);
	}
	catch {
		// Not base64.
		return true;
	}

	// No textarea fallback here: it would take the focus from whatever the user is typing in, and its blur can close an
	// edit in progress. Without a user gesture that fallback is refused anyway.
	if (navigator.clipboard?.writeText) {
		navigator.clipboard.writeText(text).catch(() => { });
	}

	return true;
}

// The printed letter where the layout has one, the physical key otherwise (for example on a Cyrillic layout).
function keyLetter(event) {
	if (event.key?.length === 1 && /[a-z]/i.test(event.key)) {
		return event.key.toLowerCase();
	}

	return event.code?.startsWith('Key') ? event.code.slice(3).toLowerCase() : '';
}

// Copies inside the key or mouse handler: it keeps the user activation some browsers require, and a large selection
// never travels through the Blazor Server circuit.
function copySelection(state, clearAfter) {
	const text = state.term.getSelection();
	if (clearAfter) {
		state.term.clearSelection();
	}

	if (text) {
		writeClipboard(text);
	}
}

function writeClipboard(text) {
	if (navigator.clipboard?.writeText) {
		navigator.clipboard.writeText(text).catch(() => copyWithCommand(text));
	}
	else {
		copyWithCommand(text);
	}
}

// navigator.clipboard only exists in secure contexts, and a web host served over plain HTTP is not one.
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
		document.execCommand('copy');
	}
	catch {
		// Clipboard access is blocked.
	}
	finally {
		textarea.remove();
		focused?.focus?.({ preventScroll: true });
	}
}

function handleMouseDown(state, event) {
	if (event.button !== 0) {
		return;
	}

	// A press starts a selection drag: the row buttons stay out of the way until it ends.
	state.pointerDown = true;
	clearHover(state);

	// Copy once the mouse is released rather than on every selection change, so a search that selects its match
	// does not overwrite the clipboard. xterm.js finishes the selection in its own mouseup listener first.
	window.addEventListener('mouseup', () => setTimeout(() => {
		state.pointerDown = false;
		if (!state.disposed && state.copyOnSelect && state.term.hasSelection()) {
			writeClipboard(state.term.getSelection());
		}
	}), { once: true });
}

// Every paste (keyboard, the browser's menu, Cmd+V) arrives here before xterm.js sees it.
function handlePasteEvent(state, event) {
	event.preventDefault();
	event.stopImmediatePropagation();
	const text = event.clipboardData?.getData('text/plain');
	if (text) {
		pasteText(state, text);
	}
}

async function pasteText(state, pasted) {
	if (state.disposed || !pasted) {
		return;
	}

	// A control character in a paste reaches the remote side as a key press. ESC [201~ ends bracketed paste early, and
	// Ctrl+O or Ctrl+U run or wipe the line without any line break for the check below to see.
	const text = pasted.replace(PasteControlCharacters, '');
	if (!text) {
		return;
	}

	if (state.confirmMultiLinePaste && /[\r\n]/.test(text)) {
		let confirmed;
		try {
			confirmed = await state.dotNet.invokeMethodAsync('ConfirmPaste', pastePreview(text), countLines(text));
		}
		catch {
			return;
		}

		if (state.disposed) {
			return;
		}

		// The dialog took focus either way.
		state.term.focus();
		if (!confirmed) {
			return;
		}
	}

	// term.paste normalizes line breaks and adds bracketed-paste markers when the remote application asked for them.
	state.term.paste(text);
}

function countLines(text) {
	const lines = text.split(/\r\n|\r|\n/);
	return lines[lines.length - 1] === '' ? lines.length - 1 : lines.length;
}

function pastePreview(text) {
	const lines = text.replace(BidiControls, '').split(/\r\n|\r|\n/, PastePreviewLines + 1);
	const preview = lines
		.slice(0, PastePreviewLines)
		.map(line => line.length > PastePreviewLineLength ? line.slice(0, safeEnd(line, PastePreviewLineLength)) + '...' : line);
	if (lines.length > PastePreviewLines && countLines(text) > PastePreviewLines) {
		preview.push('...');
	}

	return preview.join('\n');
}

function handleContextMenu(state, event) {
	event.preventDefault();
	// With mouse reporting on (tmux, htop, vim) the right click belongs to the application; Shift+right-click still
	// opens the menu.
	if (state.term.modes.mouseTrackingMode !== 'none' && !event.shiftKey) {
		return;
	}

	if (state.rightClickAction === 'paste') {
		// Read here, inside the click, because some browsers only allow clipboard reads during a user gesture.
		if (navigator.clipboard?.readText) {
			navigator.clipboard.readText().then(text => pasteText(state, text), () => { });
		}

		return;
	}

	invoke(state, 'OnContextMenu', event.clientX, event.clientY, state.term.hasSelection());
}

//#endregion

//#region Bell, links and search

function ringBell(state) {
	const now = performance.now();
	if (state.bell === 'none' || now - state.lastBell < BellThrottleMs) {
		return;
	}

	state.lastBell = now;
	if (state.bell === 'sound') {
		playBell();
		return;
	}

	state.host.classList.add(BellClass);
	clearTimeout(state.bellTimer);
	state.bellTimer = setTimeout(() => state.host.classList.remove(BellClass), BellFlashMs);
}

function playBell() {
	try {
		audioContext ??= new AudioContext();
		if (audioContext.state === 'suspended') {
			audioContext.resume().catch(() => { });
		}

		const start = audioContext.currentTime;
		const oscillator = audioContext.createOscillator();
		const gain = audioContext.createGain();
		oscillator.type = 'sine';
		oscillator.frequency.value = 880;
		gain.gain.setValueAtTime(0.0001, start);
		gain.gain.exponentialRampToValueAtTime(0.08, start + 0.01);
		gain.gain.exponentialRampToValueAtTime(0.0001, start + 0.12);
		oscillator.connect(gain).connect(audioContext.destination);
		oscillator.start(start);
		oscillator.stop(start + 0.13);
	}
	catch {
		// No audio output, or autoplay is blocked until the page gets a user gesture.
	}
}

// Links need Ctrl (Cmd on macOS), as the hover hint says and as in other terminals: a plain click on a URL is usually
// meant to focus or select. On macOS Ctrl+click is a right click, which opens the menu instead.
function openLink(event, uri) {
	if (!(IsMac ? event.metaKey : event.ctrlKey)) {
		return;
	}

	let url;
	try {
		url = new URL(uri);
	}
	catch {
		return;
	}

	if (url.protocol === 'http:' || url.protocol === 'https:') {
		window.open(url.href, '_blank', 'noopener');
	}
}

function linkLabel(uri) {
	const text = typeof uri === 'string' ? uri : '';
	return text.length > MaxLinkLabelLength ? text.slice(0, safeEnd(text, MaxLinkLabelLength)) + '...' : text;
}

function searchOptions(state, incremental, flags) {
	return {
		caseSensitive: !!flags?.caseSensitive,
		regex: !!flags?.regex,
		wholeWord: !!flags?.wholeWord,
		incremental,
		decorations: state.decorations,
	};
}

// The search addon reports results synchronously during findNext and findPrevious, so the call returns them. A pattern
// that is not a valid regular expression is reported instead of searched, which would throw.
function runSearch(state, term, flags, find) {
	if (flags?.regex && !isValidPattern(term)) {
		state.search.clearDecorations();
		state.searchResult = { index: -1, count: 0 };
		return { found: false, index: -1, count: 0, invalid: true };
	}

	// addon-search 0.16 stores the new options before comparing them with the last ones, so it only highlights again
	// when the term changes and a changed option keeps the old matches and count. Dropping its cached term forces it.
	const optionsKey = `${!!flags?.caseSensitive}|${!!flags?.wholeWord}|${!!flags?.regex}`;
	if (optionsKey !== state.searchOptionsKey) {
		state.searchOptionsKey = optionsKey;
		state.search.clearDecorations();
	}

	state.searchActive = true;
	state.inSearch = true;
	let found = false;
	try {
		found = find();
	}
	finally {
		state.inSearch = false;
	}

	return { found, index: state.searchResult.index, count: state.searchResult.count };
}

// Results also change while output arrives during a search; those updates go to .NET, coalesced.
function handleSearchResults(state, result) {
	state.searchResult = { index: result.resultIndex, count: result.resultCount };
	if (state.searchActive && !state.inSearch) {
		state.sendSearchResults(state.searchResult);
	}
}

function noSearchResult() {
	return { found: false, index: -1, count: 0 };
}

function isValidPattern(pattern) {
	try {
		new RegExp(pattern, 'g');
		return true;
	}
	catch {
		return false;
	}
}

// The scrollbar slider is the theme's foreground made translucent, so it reads on light and dark themes alike. The
// overview ruler (search matches, drawn over the scrollbar) outlines itself in white unless given a border color.
function xtermTheme(theme) {
	const foreground = firstColor(theme?.foreground);
	return foreground
		? {
			...theme,
			scrollbarSliderBackground: withAlpha(foreground, 0.16),
			scrollbarSliderHoverBackground: withAlpha(foreground, 0.28),
			scrollbarSliderActiveBackground: withAlpha(foreground, 0.4),
			overviewRulerBorder: withAlpha(foreground, 0),
		}
		: { ...theme, overviewRulerBorder: '#00000000' };
}

// #rgb, #rgba, #rrggbb or #rrggbbaa to #rrggbbaa with the given opacity.
function withAlpha(hex, alpha) {
	const digits = hex.length <= 5 ? [...hex.slice(1)].map(digit => digit + digit).join('') : hex.slice(1);
	const opacity = Math.round(alpha * 255).toString(16).padStart(2, '0');
	return `#${digits.slice(0, 6)}${opacity}`;
}

// Borders only: decoration backgrounds hide the text colors, and the active match is also selected.
function searchDecorations(theme) {
	const match = firstColor(theme?.yellow, theme?.brightYellow, theme?.foreground) ?? '#808080';
	const active = firstColor(theme?.brightRed, theme?.red, theme?.cursor) ?? match;
	return {
		matchBorder: match,
		matchOverviewRuler: match,
		activeMatchBorder: active,
		activeMatchColorOverviewRuler: active,
	};
}

function firstColor(...values) {
	return values.find(value => typeof value === 'string' && HexColor.test(value));
}

//#endregion

//#region Prompt colors

// A prompt the server sends without color gets its user, machine and folder painted through decorations, which
// recolor cells at render time without touching the text or the stream. Whatever the server colored itself is left
// as it is, so this only ever fills in what would otherwise be one flat color.
function schedulePromptScan(state) {
	if (state.disposed || state.promptColors === 'off' || state.promptFrame) {
		return;
	}

	// One pass per frame, however many chunks of output arrived in it.
	state.promptFrame = requestAnimationFrame(() => {
		state.promptFrame = 0;
		scanPrompts(state, PromptScanLimit);
	});
}

function repaintPrompts(state) {
	forgetPrompts(state);
	if (state.disposed || state.promptColors === 'off') {
		return;
	}

	cancelAnimationFrame(state.promptFrame);
	state.promptFrame = requestAnimationFrame(() => {
		state.promptFrame = 0;
		scanPrompts(state, PromptRepaintLimit);
	});
}

function scanPrompts(state, limit) {
	if (state.disposed || state.promptColors === 'off') {
		return;
	}

	const { term } = state;
	const buffer = term.buffer.active;

	// Full-screen programs draw on the alternate screen, where a line that looks like a prompt is not one.
	if (buffer.type !== 'normal') {
		return;
	}

	const cursor = buffer.baseY + buffer.cursorY;
	const anchor = state.promptAnchor && !state.promptAnchor.isDisposed ? state.promptAnchor.line : -1;
	const from = Math.max(0, cursor - limit, anchor);
	const colors = promptPalette(state);
	for (let y = from; y <= cursor; y++) {
		paintPromptLine(state, buffer, y, cursor, colors);
	}

	dropOverwrittenPrompts(state, buffer);

	// Where the next pass starts: a marker rather than a line number, because trimmed scrollback moves every line up.
	state.promptAnchor?.dispose();
	state.promptAnchor = term.registerMarker(0) ?? null;
}

function paintPromptLine(state, buffer, y, cursor, colors) {
	const line = buffer.getLine(y);
	if (!line || line.isWrapped) {
		return;
	}

	const text = line.translateToString(true);
	const existing = findPromptMark(state, y);
	if (existing) {
		if (text.startsWith(existing.prompt)) {
			return;
		}

		// The row was drawn over, so its old colors would land on new text.
		disposePromptMark(state, existing);
	}

	const match = PromptPattern.exec(text);
	if (!match) {
		return;
	}

	const [prompt, before, user, machine, separator, folder] = match;
	const segments = [
		[before.length, user.length, user === 'root' ? colors.root : colors.user],
		// The @ goes plain too, or a server that colors the whole user@host leaves its color between the two.
		[before.length + user.length, 1, colors.user],
	];
	let offset = before.length + user.length + 1;
	segments.push([offset, machine.length, colors.host]);
	if (folder) {
		offset += machine.length + separator.length;
		segments.push([offset, folder.length, colors.path]);
	}

	const marker = state.term.registerMarker(y - cursor);
	if (!marker) {
		return;
	}

	const columns = columnsOf(line, prompt.length);
	const mark = { marker, prompt, decorations: [] };
	for (const [start, length, color] of segments) {
		const x = columns[start];
		const last = columns[start + length - 1];
		if (!color || x === undefined || last === undefined) {
			continue;
		}

		const end = last + Math.max(1, line.getCell(last)?.getWidth() ?? 1);
		if (state.promptColors === 'plain' && !isPlain(line, x, end)) {
			continue;
		}

		const decoration = state.term.registerDecoration({ marker, x, width: end - x, foregroundColor: color });
		if (decoration) {
			mark.decorations.push(decoration);
		}
	}

	if (mark.decorations.length === 0) {
		marker.dispose();
		return;
	}

	marker.onDispose(() => removePromptMark(state, mark));
	insertPromptMark(state, mark);
}

// Rows on screen can be redrawn in place (clear, a prompt repainted after Ctrl+L), so the marks there are checked
// against what the row says now.
function dropOverwrittenPrompts(state, buffer) {
	const top = Math.min(buffer.viewportY, buffer.baseY);
	for (let i = state.promptMarks.length - 1; i >= 0; i--) {
		const mark = state.promptMarks[i];
		if (mark.marker.line < top) {
			break;
		}

		const text = buffer.getLine(mark.marker.line)?.translateToString(true) ?? '';
		if (!text.startsWith(mark.prompt)) {
			disposePromptMark(state, mark);
		}
	}
}

function promptPalette(state) {
	const theme = state.term.options.theme ?? {};
	return {
		// The user stays in the plain text color and the machine carries its own, as in the tab and the toolbar. A green
		// user would sit too close to the machine colors that are green or teal.
		user: firstColor(theme.foreground),
		// Root in red is the long-standing warning that the next command runs with every permission.
		root: firstColor(theme.red, theme.brightRed),
		host: resolveColor(state.host, state.promptHostColor) ?? firstColor(theme.cyan, theme.brightCyan),
		path: firstColor(theme.blue, theme.brightBlue),
	};
}

// The machine's color arrives as a CSS value, usually var(--mt-host-N) from the app's palette, while the renderers
// only take plain colors.
function resolveColor(element, value) {
	if (typeof value !== 'string' || value.trim().length === 0) {
		return undefined;
	}

	const variable = /^var\((--[\w-]+)\)$/.exec(value.trim());
	return firstColor(variable ? getComputedStyle(element).getPropertyValue(variable[1]).trim() : value.trim());
}

// translateToString joins the cells' characters, and a wide character fills two cells but one position of the
// string, so offsets are mapped back to columns before a decoration is placed.
function columnsOf(line, length) {
	const columns = [];
	for (let x = 0; x < line.length && columns.length < length; x++) {
		const cell = line.getCell(x);
		if (!cell || cell.getWidth() === 0) {
			continue;
		}

		const chars = cell.getChars() || ' ';
		for (let i = 0; i < chars.length && columns.length < length; i++) {
			columns.push(x);
		}
	}

	return columns;
}

function isPlain(line, from, to) {
	for (let x = from; x < to; x++) {
		const cell = line.getCell(x);
		if (cell && !cell.isFgDefault()) {
			return false;
		}
	}

	return true;
}

// Marks stay ordered by line, so a lookup near the cursor stops as soon as it passes the row it wants.
function findPromptMark(state, y) {
	for (let i = state.promptMarks.length - 1; i >= 0; i--) {
		const line = state.promptMarks[i].marker.line;
		if (line === y) {
			return state.promptMarks[i];
		}

		if (line < y) {
			return null;
		}
	}

	return null;
}

function insertPromptMark(state, mark) {
	const marks = state.promptMarks;
	let index = marks.length;
	while (index > 0 && marks[index - 1].marker.line > mark.marker.line) {
		index--;
	}

	marks.splice(index, 0, mark);
	while (marks.length > MaxPromptMarks) {
		disposePromptMark(state, marks[0]);
	}
}

function removePromptMark(state, mark) {
	const index = state.promptMarks.indexOf(mark);
	if (index >= 0) {
		state.promptMarks.splice(index, 1);
	}
}

function disposePromptMark(state, mark) {
	removePromptMark(state, mark);
	for (const decoration of mark.decorations) {
		disposeQuietly(decoration);
	}

	disposeQuietly(mark.marker);
}

function forgetPrompts(state) {
	for (const mark of [...state.promptMarks]) {
		disposePromptMark(state, mark);
	}

	state.promptMarks.length = 0;
	disposeQuietly(state.promptAnchor);
	state.promptAnchor = null;
}

//#endregion

//#region Loading and helpers

function enableWebgl(state, WebglAddon) {
	let addon;
	try {
		addon = new WebglAddon();
		state.term.loadAddon(addon);
	}
	catch {
		// No WebGL2 in this WebView or browser: xterm.js keeps its DOM renderer.
		disposeQuietly(addon);
		return;
	}

	state.webgl = addon;
	// Disposing the addon puts the DOM renderer back.
	state.webglLoss = addon.onContextLoss(() => disableWebgl(state));
}

function disableWebgl(state) {
	disposeQuietly(state.webglLoss);
	disposeQuietly(state.webgl);
	state.webglLoss = null;
	state.webgl = null;
}

// Imported only when a terminal wants WebGL; a failed import is retried by the next terminal.
function loadWebglAddon() {
	webglAddon ??= import('../lib/xterm/addon-webgl.js').then(
		module => module.WebglAddon,
		() => {
			webglAddon = undefined;
			return null;
		});
	return webglAddon;
}

// Opening a terminal before xterm.css applies leaves the helper textarea visible and skews the first layout.
function ensureStylesheet() {
	stylesheet ??= new Promise(resolve => {
		if (document.querySelector('link[data-mokaterm-xterm]')) {
			resolve();
			return;
		}

		const link = document.createElement('link');
		link.rel = 'stylesheet';
		link.href = new URL('../lib/xterm/xterm.css', import.meta.url).href;
		link.setAttribute('data-mokaterm-xterm', '');
		link.addEventListener('load', () => resolve(), { once: true });
		link.addEventListener('error', () => resolve(), { once: true });
		document.head.appendChild(link);
	});
	return stylesheet;
}

// xterm.js measures the cell size when it opens; a web font still loading would be measured with its fallback.
async function loadFont(fontFamily, fontSize) {
	if (!document.fonts?.load) {
		return;
	}

	try {
		await Promise.race([
			document.fonts.load(`${fontSize}px ${fontFamily}`),
			new Promise(resolve => setTimeout(resolve, FontLoadTimeoutMs)),
		]);
	}
	catch {
		// Not a valid font-family list; xterm.js falls back to its default font.
	}
}

// Sends only the newest value, one call at a time, so bursts (window resizes, title updates) collapse.
function latestSender(send) {
	let pending = null;
	let busy = false;
	return value => {
		pending = { value };
		if (busy) {
			return;
		}

		busy = true;
		(async () => {
			try {
				while (pending) {
					const next = pending.value;
					pending = null;
					await send(next);
				}
			}
			catch {
				pending = null;
			}
			finally {
				busy = false;
			}
		})();
	};
}

//#endregion
