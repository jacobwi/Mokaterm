using Microsoft.UI.Windowing;
using Mokaterm.Maui.Services;
using Windows.Graphics;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace Mokaterm.Maui.Platforms.Windows;

/// <summary>
/// Opens the main window where it last was. Kept in a file of its own beside the settings, because the window exists
/// long before the page has read anything.
/// </summary>
internal sealed class MainWindowPlacement
{
	// The title bar is what the window is dragged by, so this point of it has to be on a screen.
	private const int TitleBarMiddle = 16;

	private readonly string _file = Path.Combine(DesktopPaths.DataDirectory, "main-window.place");

	// The size to come back to from maximized, which a maximized window no longer reports.
	private RectInt32 _normal;
	private bool _maximized;

	public void Attach(WinUIWindow native)
	{
		AppWindow appWindow = native.AppWindow;
		MainWindowPlace? place = MainWindowPlace.Read(ReadFile());
		if (place is { } saved)
		{
			MoveTo(appWindow, saved);
		}

		_normal = Bounds(appWindow);
		if (place is { Maximized: true } && appWindow.Presenter is OverlappedPresenter presenter)
		{
			presenter.Maximize();
			_maximized = true;
		}

		appWindow.Changed += OnChanged;
		native.Closed += OnClosed;
	}

	private static RectInt32 Bounds(AppWindow window) =>
		new(window.Position.X, window.Position.Y, window.Size.Width, window.Size.Height);

	private static void MoveTo(AppWindow window, MainWindowPlace place)
	{
		PointInt32 grip = new(place.X + (place.Width / 2), place.Y + TitleBarMiddle);
		if (DisplayArea.GetFromPoint(grip, DisplayAreaFallback.None) is not null)
		{
			window.MoveAndResize(new RectInt32(place.X, place.Y, place.Width, place.Height));
			return;
		}

		// The screen it was on is gone: the system picks the place, and the size has to fit the screen that is left.
		RectInt32 work = DisplayArea.Primary.WorkArea;
		window.Resize(new SizeInt32(Math.Min(place.Width, work.Width), Math.Min(place.Height, work.Height)));
	}

	private void OnChanged(AppWindow sender, AppWindowChangedEventArgs args)
	{
		if ((args.DidPositionChange || args.DidSizeChange) && sender.Presenter is OverlappedPresenter presenter)
		{
			if (presenter.State == OverlappedPresenterState.Restored)
			{
				_normal = Bounds(sender);
				_maximized = false;
			}
			else if (presenter.State == OverlappedPresenterState.Maximized)
			{
				_maximized = true;
			}
		}
	}

	private void OnClosed(object sender, Microsoft.UI.Xaml.WindowEventArgs args)
	{
		if (sender is WinUIWindow native)
		{
			native.AppWindow.Changed -= OnChanged;
			native.Closed -= OnClosed;
		}

		WriteFile(new MainWindowPlace(_normal.X, _normal.Y, _normal.Width, _normal.Height, _maximized).Write());
	}

	// Losing this file only costs the next start its place, so a disk that refuses is not worth reporting.
	private string? ReadFile()
	{
		try
		{
			return File.Exists(_file) ? File.ReadAllText(_file) : null;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private void WriteFile(string text)
	{
		try
		{
			File.WriteAllText(_file, text);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return;
		}
	}
}
