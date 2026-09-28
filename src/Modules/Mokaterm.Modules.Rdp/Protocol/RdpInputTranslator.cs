namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// Turns page events into operations for the session, and remembers what is held down. A view that loses focus or
/// goes away leaves keys pressed on the server otherwise, which is how a remote desktop ends up typing by itself.
/// </summary>
internal sealed class RdpInputTranslator
{
	/// <summary>One wheel notch, the unit RDP counts in.</summary>
	public const int WheelNotch = 120;

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

	private static void TranslateWheel(RdpInputEvent input, List<RdpOperation> operations)
	{
		// The page counts the way a document scrolls, down and right positive; RDP counts the way the wheel turns.
		int units = Math.Clamp(-input.B, short.MinValue, short.MaxValue);
		if (units != 0)
		{
			operations.Add(new RdpOperation(RdpOperationKind.Wheel, input.A != 0 ? 1 : 0, units));
		}
	}

	private static bool IsButton(int button) => button is >= 0 and <= 4;
}
