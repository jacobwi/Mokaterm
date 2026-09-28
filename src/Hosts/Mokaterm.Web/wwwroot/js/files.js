// Browser side of BrowserLocalFileAccess. Picked File objects stay here; .NET pulls each one as a stream when the
// upload actually starts, so nothing is read into memory upfront.
//
// A pick lives until .NET releases it, which it does once the uploads that read it are over, the same way the file drop
// bridge (filedrop.js) keeps dropped files. The timer only covers a pick .NET never took, such as one whose call was
// cancelled while the picker was open: a queued upload may reach its file long after an hour.

const picks = new Map();
const unclaimedLifetimeMs = 10 * 60 * 1000;

function newPickId() {
	if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
		return crypto.randomUUID();
	}

	// randomUUID needs a secure context; a plain-http host still works with a less random id.
	return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}

export function pickFiles(multiple, directory) {
	return new Promise(resolve => {
		const input = document.createElement('input');
		input.type = 'file';
		input.multiple = multiple || directory;
		if (directory) {
			input.webkitdirectory = true;
		}

		input.style.display = 'none';

		const finish = files => {
			input.remove();
			if (files.length === 0) {
				resolve({ id: null, files: [] });
				return;
			}

			const id = newPickId();
			picks.set(id, { files, timer: setTimeout(() => picks.delete(id), unclaimedLifetimeMs) });
			resolve({
				id,
				files: files.map(file => ({
					name: file.name,
					relativePath: file.webkitRelativePath || file.name,
					size: file.size,
					lastModified: file.lastModified,
				})),
			});
		};

		input.addEventListener('change', () => finish(Array.from(input.files || [])), { once: true });
		input.addEventListener('cancel', () => finish([]), { once: true });
		document.body.appendChild(input);
		input.click();
	});
}

// .NET has the pick; from here only release() lets go of it.
export function hold(id) {
	const pick = picks.get(id);
	if (pick) {
		clearTimeout(pick.timer);
		pick.timer = 0;
	}
}

export function release(id) {
	const pick = picks.get(id);
	if (pick) {
		clearTimeout(pick.timer);
		picks.delete(id);
	}
}

export function getFile(id, index) {
	const file = picks.get(id)?.files[index];
	if (!file) {
		throw new Error('The picked file is no longer available. Pick it again.');
	}

	return file;
}

export function download(url) {
	const anchor = document.createElement('a');
	anchor.href = url;
	anchor.download = '';
	anchor.rel = 'noopener';
	anchor.style.display = 'none';
	document.body.appendChild(anchor);
	anchor.click();
	anchor.remove();
}
