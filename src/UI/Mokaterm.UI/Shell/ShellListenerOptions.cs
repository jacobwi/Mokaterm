namespace Mokaterm.UI.Shell;

/// <summary>Options for the window-level listeners in <c>shell.js</c>. Serialized to camelCase JSON.</summary>
internal sealed record ShellListenerOptions(
	IReadOnlyList<ShellShortcutBinding> Shortcuts,
	int ActivityIntervalMs,
	bool WatchVisibility,
	bool ConfirmLeave);

/// <summary>One key combination the script intercepts, identified by <see cref="Id"/>.</summary>
internal sealed record ShellShortcutBinding(string Id, string Code, bool Shift);
