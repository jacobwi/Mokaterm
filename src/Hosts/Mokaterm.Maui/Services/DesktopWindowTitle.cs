using Mokaterm.UI.Common.Platform;

namespace Mokaterm.Maui.Services;

// The main window's own title, which the taskbar, Alt+Tab and Task Manager show even though the page draws the title
// bar. Tool windows set theirs when they open and never come here: only the main window's shell names a session.
public sealed class DesktopWindowTitle : IWindowTitle
{
	public const string AppName = "Mokaterm";

	private readonly Lock _lock = new();
	private Window? _window;
	private string _title = AppName;

	public void Attach(Window window)
	{
		ArgumentNullException.ThrowIfNull(window);
		lock (_lock)
		{
			_window = window;
		}

		Apply();
	}

	public Task ShowAsync(string? subject)
	{
		lock (_lock)
		{
			_title = string.IsNullOrWhiteSpace(subject) ? AppName : $"{AppName} · {subject}";
		}

		Apply();
		return Task.CompletedTask;
	}

	private void Apply()
	{
		Window? window;
		lock (_lock)
		{
			window = _window;
		}

		if (window is null)
		{
			return;
		}

		if (window.Dispatcher.IsDispatchRequired)
		{
			_ = window.Dispatcher.Dispatch(() => SetTitle(window));
		}
		else
		{
			SetTitle(window);
		}
	}

	// Read under the lock at the moment it is written, so a title queued behind a newer one cannot win.
	private void SetTitle(Window window)
	{
		string title;
		lock (_lock)
		{
			title = _title;
		}

		window.Title = title;
	}
}
