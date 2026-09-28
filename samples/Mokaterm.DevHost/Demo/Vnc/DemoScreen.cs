using System.Globalization;

namespace Mokaterm.DevHost.Demo.Vnc;

/// <summary>
/// The framebuffer behind the demo VNC server: a test pattern, a clock, a box that moves on its own and marks from
/// the pointer and the keyboard, so every part of the session view can be seen working.
/// </summary>
internal sealed class DemoScreen : IAsyncDisposable
{
	public const int Width = 800;

	public const int Height = 600;

	public const string DesktopName = "mokaterm demo :1";

	private const uint Background = 0x060608;
	private const uint Grid = 0x241416;
	private const uint Red = 0xEF5350;
	private const uint Text = 0xE8E8EC;
	private const uint Dim = 0x6A6A74;
	private const uint Box = 0xC62828;
	private const uint Mark = 0x00E676;

	/// <summary>Marks from clicks. Old ones fall off so the screen does not fill up.</summary>
	private const int MaxMarks = 40;

	private const int MarkSize = 5;

	private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(120);

	private static readonly (string Label, uint Colour)[] Bars =
	[
		("R", 0xEF5350),
		("G", 0x00E676),
		("B", 0x42A5F5),
		("C", 0x26C6DA),
		("M", 0xCE93D8),
		("Y", 0xFFD54F),
		("W", 0xE8E8EC),
		("K", 0x101015),
	];

	private readonly uint[] _pixels = new uint[Width * Height];

	/// <summary>The test pattern without anything that moves, so repainting under the pointer puts it back.</summary>
	private readonly uint[] _pattern = new uint[Width * Height];

	private readonly List<DemoRect> _marks = [];
	private readonly Lock _gate = new();
	private readonly TimeProvider _timeProvider;
	private readonly CancellationTokenSource _stopping = new();
	private readonly DateTimeOffset _started;
	private Task _animation = Task.CompletedTask;
	private int _boxX = 60;
	private int _boxY = 320;
	private int _boxDx = 4;
	private int _boxDy = 3;
	private int _pointerX = -1;
	private int _pointerY = -1;
	private long _lastClockSecond = -1;
	private string _keyLabel = "NONE";
	private string _clipboard = "";

	public DemoScreen(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider;
		_started = timeProvider.GetUtcNow();
		DrawStaticPattern();
	}

	/// <summary>Raised for every part of the screen that changed. Handlers run on the animation or an input thread.</summary>
	public event Action<DemoRect>? Damaged;

	public void Start() => _animation = RunAsync(_stopping.Token);

	/// <summary>Copies <paramref name="rect"/> into <paramref name="destination"/>, row by row.</summary>
	public void Read(DemoRect rect, Span<uint> destination)
	{
		lock (_gate)
		{
			for (int row = 0; row < rect.Height; row++)
			{
				_pixels.AsSpan(((rect.Y + row) * Width) + rect.X, rect.Width).CopyTo(destination[(row * rect.Width)..]);
			}
		}
	}

	public void MovePointer(int x, int y, bool pressed)
	{
		DemoRect damage;
		lock (_gate)
		{
			damage = ClearPointer();
			_pointerX = Math.Clamp(x, 0, Width - 1);
			_pointerY = Math.Clamp(y, 0, Height - 1);
			if (pressed)
			{
				// A click leaves a mark, so a viewer can tell its input arrived even after the pointer moved on.
				DemoRect mark = new(_pointerX - (MarkSize / 2), _pointerY - (MarkSize / 2), MarkSize, MarkSize);
				if (_marks.Count == MaxMarks)
				{
					// The oldest mark stays on screen until something repaints over it.
					_marks.RemoveAt(0);
				}

				_marks.Add(mark);
				FillRect(mark, Mark);
				damage = damage.Union(mark);
			}

			damage = damage.Union(DrawPointer());
		}

		Damage(damage);
	}

	public void ShowKey(string label)
	{
		lock (_gate)
		{
			_keyLabel = label.Length == 0 ? "NONE" : label;
			DrawStatus();
		}

		Damage(StatusRect);
	}

	public void SetClipboard(string text)
	{
		lock (_gate)
		{
			_clipboard = text;
			DrawStatus();
		}

		Damage(StatusRect);
	}

	public async ValueTask DisposeAsync()
	{
		await _stopping.CancelAsync();
		try
		{
			await _animation;
		}
		catch (OperationCanceledException)
		{
			// Stopping.
		}

		_stopping.Dispose();
	}

	private static DemoRect StatusRect => new(40, 520, Width - 80, 40);

	private static DemoRect ClockRect => new(40, 120, 420, 40);

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		try
		{
			using PeriodicTimer timer = new(FrameInterval, _timeProvider);
			while (await timer.WaitForNextTickAsync(cancellationToken))
			{
				DemoRect damage;
				lock (_gate)
				{
					damage = MoveBox();
					long second = (long)(_timeProvider.GetUtcNow() - _started).TotalSeconds;
					if (second != _lastClockSecond)
					{
						_lastClockSecond = second;
						DrawClock(second);
						damage = damage.Union(ClockRect);
					}
				}

				Damage(damage);
			}
		}
		catch (OperationCanceledException)
		{
			// Stopping.
		}
	}

	private void Damage(DemoRect rect)
	{
		DemoRect clamped = rect.Clamp(Width, Height);
		if (!clamped.IsEmpty)
		{
			Damaged?.Invoke(clamped);
		}
	}

	private DemoRect MoveBox()
	{
		DemoRect before = new(_boxX, _boxY, 64, 64);
		Repaint(before);

		_boxX += _boxDx;
		_boxY += _boxDy;
		if (_boxX <= 40 || _boxX + 64 >= Width - 40)
		{
			_boxDx = -_boxDx;
			_boxX = Math.Clamp(_boxX, 40, Width - 104);
		}

		if (_boxY <= 300 || _boxY + 64 >= 500)
		{
			_boxDy = -_boxDy;
			_boxY = Math.Clamp(_boxY, 300, 436);
		}

		DemoRect after = new(_boxX, _boxY, 64, 64);
		FillRect(after, Box);
		FillRect(new DemoRect(_boxX + 8, _boxY + 8, 48, 48), Background);
		DrawText(_boxX + 20, _boxY + 28, "VNC", Red, 1);
		return before.Union(after);
	}

	private void DrawStaticPattern()
	{
		FillRect(new DemoRect(0, 0, Width, Height), Background);
		DrawGrid(new DemoRect(0, 0, Width, Height));
		DrawText(40, 40, "MOKATERM VNC DEMO", Red, 4);
		DrawText(40, 90, "RAW ENCODING - 800X600 - LOOPBACK", Dim, 2);

		int barWidth = (Width - 80) / Bars.Length;
		for (int i = 0; i < Bars.Length; i++)
		{
			DemoRect bar = new(40 + (i * barWidth), 190, barWidth - 4, 60);
			FillRect(bar, Bars[i].Colour);
			DrawText(bar.X + 6, bar.Y + 68, Bars[i].Label, Text, 2);
		}

		// A frame around the screen, so scaling and 1:1 are easy to tell apart.
		DrawRect(new DemoRect(8, 8, Width - 16, Height - 16), Red);
		DrawText(40, 280, "MOVE THE POINTER, CLICK, TYPE", Text, 2);

		// Everything drawn from here on moves or changes, so it stays out of the pattern.
		_pixels.CopyTo(_pattern, 0);
		DrawClock(0);
		DrawStatus();
	}

	private void DrawGrid(DemoRect area)
	{
		for (int y = area.Y; y < area.Bottom; y++)
		{
			for (int x = area.X; x < area.Right; x++)
			{
				if (x % 40 == 0 || y % 40 == 0)
				{
					_pixels[(y * Width) + x] = Grid;
				}
			}
		}
	}

	private void DrawClock(long seconds)
	{
		Repaint(ClockRect);
		string text = string.Create(
			CultureInfo.InvariantCulture,
			$"UPTIME {seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}");
		DrawText(ClockRect.X, ClockRect.Y + 8, text, Text, 3);
	}

	private void DrawStatus()
	{
		Repaint(StatusRect);
		string clipboard = _clipboard.Length == 0 ? "NONE" : Shorten(_clipboard);
		DrawText(StatusRect.X, StatusRect.Y + 4, "KEY " + _keyLabel, Mark, 2);
		DrawText(StatusRect.X, StatusRect.Y + 22, "CLIPBOARD " + clipboard, Dim, 2);
	}

	private static string Shorten(string text)
	{
		string line = text.ReplaceLineEndings(" ");
		return line.Length <= 28 ? line : line[..28] + "...";
	}

	private DemoRect ClearPointer()
	{
		if (_pointerX < 0)
		{
			return default;
		}

		DemoRect clamped = new DemoRect(_pointerX - 10, _pointerY - 10, 21, 21).Clamp(Width, Height);
		Repaint(clamped);
		return clamped;
	}

	/// <summary>Puts back what belongs under a moving part: the test pattern and the click marks.</summary>
	private void Repaint(DemoRect rect)
	{
		DemoRect area = rect.Clamp(Width, Height);
		for (int row = 0; row < area.Height; row++)
		{
			int start = ((area.Y + row) * Width) + area.X;
			_pattern.AsSpan(start, area.Width).CopyTo(_pixels.AsSpan(start, area.Width));
		}

		foreach (DemoRect mark in _marks)
		{
			if (mark.X < area.Right && mark.Right > area.X && mark.Y < area.Bottom && mark.Bottom > area.Y)
			{
				FillRect(mark, Mark);
			}
		}
	}

	private DemoRect DrawPointer()
	{
		FillRect(new DemoRect(_pointerX - 10, _pointerY, 21, 1).Clamp(Width, Height), Red);
		FillRect(new DemoRect(_pointerX, _pointerY - 10, 1, 21).Clamp(Width, Height), Red);
		return new DemoRect(_pointerX - 10, _pointerY - 10, 21, 21).Clamp(Width, Height);
	}

	private void FillRect(DemoRect rect, uint colour)
	{
		DemoRect area = rect.Clamp(Width, Height);
		for (int y = area.Y; y < area.Bottom; y++)
		{
			_pixels.AsSpan((y * Width) + area.X, area.Width).Fill(colour);
		}
	}

	private void DrawRect(DemoRect rect, uint colour)
	{
		FillRect(new DemoRect(rect.X, rect.Y, rect.Width, 1), colour);
		FillRect(new DemoRect(rect.X, rect.Bottom - 1, rect.Width, 1), colour);
		FillRect(new DemoRect(rect.X, rect.Y, 1, rect.Height), colour);
		FillRect(new DemoRect(rect.Right - 1, rect.Y, 1, rect.Height), colour);
	}

	private void DrawText(int x, int y, string text, uint colour, int scale)
	{
		int cursor = x;
		foreach (char character in text)
		{
			for (int column = 0; column < DemoFont.GlyphWidth; column++)
			{
				for (int row = 0; row < DemoFont.GlyphHeight; row++)
				{
					if (DemoFont.IsSet(character, column, row))
					{
						FillRect(new DemoRect(cursor + (column * scale), y + (row * scale), scale, scale), colour);
					}
				}
			}

			cursor += (DemoFont.GlyphWidth + 1) * scale;
		}
	}
}
