// OS file drops for the browser file drop bridge. Dropped File objects stay in this module under a drop id; .NET pulls
// each one as a stream when its upload starts, so nothing is read into memory up front.

import { attachDropZone, newDropId } from '../../Mokaterm.UI.Common/js/dropzone.js';

const drops = new Map();
const dropLifetimeMs = 60 * 60 * 1000;

// Metadata goes to .NET in chunks so a large folder never exceeds the circuit's incoming message limit.
const chunkSize = 100;

// Entries must be taken while the drop event is being handled: the DataTransfer is emptied once the handler returns.
function takeEntries(dataTransfer) {
	const entries = [];
	const files = [];
	const items = Array.from(dataTransfer.items || []);
	if (items.length === 0) {
		files.push(...Array.from(dataTransfer.files || []));
		return { entries, files };
	}

	for (const item of items) {
		if (item.kind !== 'file') {
			continue;
		}

		const entry = typeof item.webkitGetAsEntry === 'function' ? item.webkitGetAsEntry() : null;
		if (entry) {
			entries.push(entry);
		} else {
			const file = item.getAsFile();
			if (file) {
				files.push(file);
			}
		}
	}

	return { entries, files };
}

function readFile(entry) {
	return new Promise((resolve, reject) => entry.file(resolve, reject));
}

function readBatch(reader) {
	return new Promise((resolve, reject) => reader.readEntries(resolve, reject));
}

// readEntries returns at most about a hundred entries per call; an empty batch means the folder is done.
async function readDirectory(entry) {
	const reader = entry.createReader();
	const children = [];
	for (;;) {
		const batch = await readBatch(reader);
		if (batch.length === 0) {
			return children;
		}

		children.push(...batch);
	}
}

function relativePathOf(entry, fallback) {
	const path = (entry.fullPath || '').replace(/^\/+/, '');
	return path || fallback;
}

// Walks the dropped entries depth first, listing each folder before its contents.
async function collect(entries, looseFiles) {
	const collected = [];
	const pending = [...entries].reverse();
	while (pending.length > 0) {
		const entry = pending.pop();
		try {
			if (entry.isFile) {
				const file = await readFile(entry);
				collected.push({ file, name: file.name, relativePath: relativePathOf(entry, file.name), isDirectory: false });
			} else if (entry.isDirectory) {
				collected.push({ file: null, name: entry.name, relativePath: relativePathOf(entry, entry.name), isDirectory: true });
				const children = await readDirectory(entry);
				for (let index = children.length - 1; index >= 0; index--) {
					pending.push(children[index]);
				}
			}
		} catch {
			// An entry that cannot be read (permissions, removed meanwhile) is skipped; the rest of the drop uploads.
		}
	}

	for (const file of looseFiles) {
		collected.push({ file, name: file.name, relativePath: file.name, isDirectory: false });
	}

	return collected;
}

async function send(dotNetRef, entries, looseFiles, ctrlKey, shiftKey, altKey) {
	const collected = await collect(entries, looseFiles);
	if (collected.length === 0) {
		return;
	}

	const id = newDropId();
	const files = [];
	const metadata = collected.map(item => {
		const fileIndex = item.isDirectory ? -1 : files.push(item.file) - 1;
		return {
			name: item.name,
			relativePath: item.relativePath,
			isDirectory: item.isDirectory,
			size: item.file ? item.file.size : 0,
			lastModified: item.file ? item.file.lastModified : 0,
			fileIndex,
		};
	});

	drops.set(id, { files, timer: setTimeout(() => drops.delete(id), dropLifetimeMs) });

	try {
		for (let start = 0; start < metadata.length; start += chunkSize) {
			await dotNetRef.invokeMethodAsync('AddDropItems', id, metadata.slice(start, start + chunkSize));
		}

		// From here .NET releases the files once the uploads finished, which can take longer than the timer: a queued file
		// must still be there when its turn comes.
		clearTimeout(drops.get(id)?.timer);

		// Resolves only after the uploads finished, which is when .NET releases the files.
		await dotNetRef.invokeMethodAsync('OnDrop', id, ctrlKey, shiftKey, altKey);
	} catch {
		release(id);
	}
}

export function attach(element, dotNetRef) {
	return attachDropZone(
		element,
		over => dotNetRef.invokeMethodAsync('OnDragOver', over).catch(() => { }),
		event => {
			const { entries, files } = takeEntries(event.dataTransfer);
			send(dotNetRef, entries, files, event.ctrlKey || event.metaKey, event.shiftKey, event.altKey);
		});
}

export function getFile(dropId, index) {
	const file = drops.get(dropId)?.files[index];
	if (!file) {
		throw new Error('The dropped file is no longer available. Drop it again.');
	}

	return file;
}

export function release(dropId) {
	const drop = drops.get(dropId);
	if (drop) {
		clearTimeout(drop.timer);
		drops.delete(dropId);
	}
}
