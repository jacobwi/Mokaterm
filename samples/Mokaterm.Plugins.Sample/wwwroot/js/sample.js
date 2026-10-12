export function stamp(element) {
	element.textContent = "The plugin's script ran.";
	element.dataset.loaded = "true";
}
