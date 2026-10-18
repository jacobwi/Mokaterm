namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// Turns page events into operations for the session, and remembers what is held down. A view that loses focus or
/// goes away leaves keys pressed on the server otherwise, which is how a remote desktop ends up typing by itself.
/// </summary>
internal sealed class RdpInputTranslator
{
	/// <summary>One wheel notch, the unit RDP counts in.</summary>
	public const int WheelNotch = 120;

	/// <summary>The most one pointer event carries: its rotation field is nine bits of two's complement.</summary>
	public const int MaxUnitsPerEvent = 255;

	/// <summary>
	/// The most one page event may turn the wheel. A fling on a touchpad can report thousands of pixels at once, and
	/// each 255 units is an event of its own on the wire.
	/// </summary>
	public const int MaxWheelUnits = 16 * WheelNotch;

	private readonly List<RdpScancode> _heldKeys = [];
	private readonly List<int> _heldButtons = [];
	private int _width;
	private int _height;
	private int _x;
	private int _y;
	private bool _hasPosition;

	public RdpInputTranslator(int width, int height) => Resize(width, height);

	/// <summary>Keys the server currently sees as pressed, oldest first.</summary>
	public IReadOnlyList<RdpScancode> HeldKeys => _heldKeys;

	public IReadOnlyList<int> HeldButtons => _heldButtons;

	/// <summary>The desktop got a new size, so pointer events are clamped to it from now on.</summary>
	public void Resize(int width, int height)
	{
		_width = Math.Max(1, width);
		_height = Math.Max(1, height);
		if (_hasPosition)
		{
			_x = Math.Clamp(_x, 0, _width - 1);
			_y = Math.Clamp(_y, 0, _height - 1);
		}
	}

	/// <summary>The server moved the pointer, so the next move from the page is measured against this.</summary>
	public void PointerMovedByServer(int x, int y)
	{
		_x = Math.Clamp(x, 0, _width - 1);
		_y = Math.Clamp(y, 0, _height - 1);
		_hasPosition = true;
	}

	public IReadOnlyList<RdpOperation> Translate(IReadOnlyList<RdpInputEvent> events)
	{
		ArgumentNullException.ThrowIfNull(events);
		List<RdpOperation> operations = new(events.Count);
		foreach (RdpInputEvent input in events)
		{
			Translate(input, operations);
		}

		return operations;
	}

	/// <summary>Everything still held, released newest first so modifiers go up last.</summary>
	public IReadOnlyList<RdpOperation> ReleaseEverything()
	{
		List<RdpOperation> operations = new(_heldKeys.Count + _heldButtons.Count);
		for (int i = _heldButtons.Count - 1; i >= 0; i--)
		{
			operations.Add(new RdpOperation(RdpOperationKind.MouseUp, _heldButtons[i]));
		}

		for (int i = _heldKeys.Count - 1; i >= 0; i--)
		{
			operations.Add(new RdpOperation(RdpOperationKind.KeyUp, _heldKeys[i].Value));
		}

		_heldButtons.Clear();
		_heldKeys.Clear();
		return operations;
	}

	private void Translate(RdpInputEvent input, List<RdpOperation> operations)
	{
		switch (input.Kind)
		{
			case RdpInputKind.MouseMove:
				TranslateMove(input, operations);
				break;
			case RdpInputKind.MouseDown when IsButton(input.A):
				if (!_heldButtons.Contains(input.A))
				{
					_heldButtons.Add(input.A);
				}

				operations.Add(new RdpOperation(RdpOperationKind.MouseDown, input.A));
				break;
			case RdpInputKind.MouseUp when IsButton(input.A):
				_heldButtons.Remove(input.A);
				operations.Add(new RdpOperation(RdpOperationKind.MouseUp, input.A));
				break;
			case RdpInputKind.Wheel:
				TranslateWheel(input, operations);
				break;
			case RdpInputKind.KeyDown when RdpKeyboardMap.TryGet(input.Code, out RdpScancode down):
				if (!_heldKeys.Contains(down))
				{
					_heldKeys.Add(down);
				}

				operations.Add(new RdpOperation(RdpOperationKind.KeyDown, down.Value));
				break;
			case RdpInputKind.KeyUp when RdpKeyboardMap.TryGet(input.Code, out RdpScancode up):
				_heldKeys.Remove(up);
				operations.Add(new RdpOperation(RdpOperationKind.KeyUp, up.Value));
				break;
			case RdpInputKind.ReleaseKeys:
				operations.AddRange(ReleaseEverything());
				break;
			default:
				// A button out of range or a key with no scancode, such as a media key.
				break;
		}
	}

	private void TranslateMove(RdpInputEvent input, List<RdpOperation> operations)
	{
		int x = Math.Clamp(input.A, 0, _width - 1);
		int y = Math.Clamp(input.B, 0, _height - 1);
		if (_hasPosition && x == _x && y == _y)
		{
			return;
		}

		_x = x;
		_y = y;
		_hasPosition = true;
		operations.Add(new RdpOperation(RdpOperationKind.MouseMove, x, y));
	}

	/// <summary>
	/// The page reports a wheel the way a document scrolls, down and right positive (<c>WheelEvent.deltaY</c> and
	/// <c>deltaX</c>). MS-RDPBCGR only says the rotation is negative when PTRFLAGS_WHEEL_NEGATIVE is set; what the sign
	/// means is Windows' own, because the server injects the value as it comes: for <c>MOUSEEVENTF_WHEEL</c> positive is
	/// the wheel turned away from the user, which scrolls up, and for <c>MOUSEEVENTF_HWHEEL</c> positive is the wheel
	/// tilted right. So the vertical axis turns around and the horizontal one does not. FreeRDP's clients agree: X11
	/// button 7 (scroll right) is PTR_FLAGS_HWHEEL with a positive 0x78 and button 6 (left) the negative one, and the
	/// Windows client passes WM_MOUSEHWHEEL's delta through unchanged. IronRDP's own web client negates both axes, which
	/// reverses its horizontal scrolling, so it is not followed here.
	/// </summary>
	private static void TranslateWheel(RdpInputEvent input, List<RdpOperation> operations)
	{
		bool vertical = input.A != 0;
		long requested = vertical ? -(long)input.B : input.B;
		int units = (int)Math.Clamp(requested, -MaxWheelUnits, MaxWheelUnits);

		// The rotation travels in nine bits of two's complement, and IronRDP keeps the low eight of whatever it is given
		// and sets the sign flag apart, so 360 would arrive as 104 and 256 as nothing at all. FreeRDP's SDL client sends a
		// large turn as several events of at most 255 for the same reason.
		while (units != 0)
		{
			int step = Math.Clamp(units, -MaxUnitsPerEvent, MaxUnitsPerEvent);
			operations.Add(new RdpOperation(RdpOperationKind.Wheel, vertical ? 1 : 0, step));
			units -= step;
		}
	}

	private static bool IsButton(int button) => button is >= 0 and <= 4;
}
