// Buttons of the reconnect dialog in App.razor. The CSP allows no inline handlers, so they are wired here.
document.addEventListener('click', event => {
	const button = event.target instanceof Element ? event.target.closest('[data-reconnect-action]') : null;
	if (!button) {
		return;
	}

	if (button.getAttribute('data-reconnect-action') === 'reload') {
		location.reload();
		return;
	}

	const modal = document.getElementById('components-reconnect-modal');
	const paused = !!modal && (modal.classList.contains('components-reconnect-paused') || modal.classList.contains('components-reconnect-resume-failed'));
	const attempt = paused && typeof Blazor.resumeCircuit === 'function' ? Blazor.resumeCircuit() : Blazor.reconnect();

	// False means the server no longer has the circuit, so only a reload helps.
	Promise.resolve(attempt).then(
		connected => {
			if (connected === false) {
				location.reload();
			}
		},
		() => location.reload());
});
