namespace Mokaterm.UI.Shell;

/// <summary>
/// Whether the main window's page can be seen: false while the browser tab is in the background or the desktop window
/// is minimized. <see cref="ShellInputBridge"/> sets it from the shell's own visibility listener; what reads a server
/// for the eye alone, such as the status bar's live figures, stops while it is false.
/// </summary>
internal sealed class PageVisibility
{
	private volatile bool _visible = true;

	/// <summary>Raised when <see cref="IsVisible"/> changes. It may fire on any thread.</summary>
	public event Action? Changed;

	public bool IsVisible => _visible;

	public void Set(bool visible)
	{
		if (_visible == visible)
		{
			return;
		}

		_visible = visible;
		Changed?.Invoke();
	}
}
