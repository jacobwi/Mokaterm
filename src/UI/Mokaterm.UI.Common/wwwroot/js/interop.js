// Page-side plumbing shared by every view that owns one instance per element (the terminal, the VNC and RDP screens)
// and by the shell and the file browser: the instance map, calls back to .NET, listeners that come back as disposables,
// the bounded queues that carry input to .NET, and the attach-once marker.
//
// Served as _content/Mokaterm.UI.Common/js/interop.js. Views import it with a relative path
// (../../Mokaterm.UI.Common/js/interop.js) so a host under a path base resolves it too, and every importer shares the
// one module instance.

/**
 * One instance map per module. `add` gives a state the next id, `get` finds a live one, and `release` takes it out of
 * the map, marks it disposed and disposes what it collected in `state.disposables`. Whatever else a view built, a
 * canvas or a connection, is its own to release after that.
 */
export function createInstances() {
	const live = new Map();
	let nextId = 1;
	return {
		add(state) {
			state.id = nextId++;
			live.set(state.id, state);
			return state.id;
		},

		get(id) {
			return live.get(id);
		},

		/** The state that was under id, or null when it is already gone. */
		release(id) {
			const state = live.get(id);
			if (!state) {
				return null;
			}

			live.delete(id);
			state.disposed = true;
			for (const disposable of state.disposables) {
				disposeQuietly(disposable);
			}

			state.disposables.length = 0;
			return state;
		},
	};
}

/** Tells the view's .NET side about something that happened in the page. Nothing waits for the answer. */
export function invoke(state, method, ...args) {
	if (!state.disposed) {
		state.dotNet.invokeMethodAsync(method, ...args).catch(() => {
			// The view or its circuit is gone.
		});
	}
}

/**
 * Adds a listener and returns how to take it off again, in the shape xterm.js and noVNC give their own subscriptions, so
 * one teardown loop fits both. `options` is a capture flag or an options object, whichever the listener needs.
 */
export function listen(target, type, handler, options = false) {
	target.addEventListener(type, handler, options);
	return { dispose: () => target.removeEventListener(type, handler, options) };
}

/** Disposes what a view collected without letting one broken subscription stop the rest of its teardown. */
export function disposeQuietly(disposable) {
	try {
		disposable?.dispose();
	}
	catch {
		// Already disposed or never fully created.
	}
}

// Which elements already carry which listeners, by key. A WeakSet leaves the element itself untouched and lets it be
// collected with the page.
const attachments = new Map();

/**
 * Runs `attach` for this element once per key, so a component that renders again does not end up with the listener
 * twice. Returns whether it ran.
 */
export function attachOnce(element, key, attach) {
	if (!element) {
		return false;
	}

	let attached = attachments.get(key);
	if (!attached) {
		attached = new WeakSet();
		attachments.set(key, attached);
	}

	if (attached.has(element)) {
		return false;
	}

	attached.add(element);
	attach(element);
	return true;
}

/** A queue of events for .NET, cut into calls of at most `limit` events; the caller's constant says why that many. */
export function eventQueue(method, limit) {
	const waiting = [];
	return {
		method,
		sending: false,
		push(event) {
			waiting.push(event);
		},

		/** The event queued last, for a caller that folds a new one into it instead of queueing another. */
		last() {
			return waiting.length > 0 ? waiting[waiting.length - 1] : null;
		},

		pending() {
			return waiting.length > 0;
		},

		take() {
			return waiting.splice(0, limit);
		},

		drop() {
			waiting.length = 0;
		},
	};
}

/**
 * A queue of bytes for .NET, coalesced and cut into calls of at most `limit` bytes whatever sizes they were pushed in,
 * so one long message and a burst of short ones both cross the same way.
 */
export function byteQueue(method, limit) {
	let chunks = [];
	let length = 0;
	return {
		method,
		sending: false,
		push(bytes) {
			if (bytes.length > 0) {
				chunks.push(bytes);
				length += bytes.length;
			}
		},

		pending() {
			return length > 0;
		},

		take() {
			const size = Math.min(length, limit);
			const chunk = new Uint8Array(size);
			let offset = 0;
			while (offset < size) {
				const head = chunks[0];
				const take = Math.min(head.length, size - offset);
				chunk.set(head.subarray(0, take), offset);
				offset += take;
				if (take === head.length) {
					chunks.shift();
				}
				else {
					chunks[0] = head.subarray(take);
				}
			}

			length -= size;
			return chunk;
		},

		drop() {
			chunks = [];
			length = 0;
		},
	};
}

/**
 * Sends what `queue` holds to .NET, one call at a time, so a burst travels as a few messages instead of one per event.
 * The busy flag is checked here and nowhere else, so a second entry point can pump a queue that is already draining
 * without starting a second loop.
 */
export async function drain(state, queue) {
	if (queue.sending || state.disposed || !queue.pending()) {
		return;
	}

	queue.sending = true;
	try {
		while (queue.pending() && !state.disposed) {
			await state.dotNet.invokeMethodAsync(queue.method, queue.take());
		}
	}
	catch {
		// The .NET side is gone; what is queued has nowhere to go.
		queue.drop();
	}
	finally {
		queue.sending = false;
	}
}
