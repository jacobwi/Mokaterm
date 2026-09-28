using Devolutions.IronRdp;

namespace Mokaterm.Modules.Rdp.Protocol;

/// <summary>
/// An IronRDP input operation together with the handle it came from. Both are native objects, and nothing says the
/// operation stops needing the scancode or position it was built from, so they are released together.
/// </summary>
internal sealed class RdpOperationHandle : IDisposable
{
	private readonly IDisposable _source;

	private RdpOperationHandle(Operation operation, IDisposable source)
	{
		Operation = operation;
		_source = source;
	}

	public Operation Operation { get; }

	/// <exception cref="ArgumentOutOfRangeException">The operation carries a value the protocol has no room for.</exception>
	public static RdpOperationHandle Create(RdpOperation operation)
	{
		switch (operation.Kind)
		{
			case RdpOperationKind.MouseMove:
			{
				MousePosition position = MousePosition.New(ToUInt16(operation.A), ToUInt16(operation.B));
				return new RdpOperationHandle(position.AsMoveOperation(), position);
			}

			case RdpOperationKind.MouseDown:
			{
				MouseButton button = MouseButton.New(ToButton(operation.A));
				return new RdpOperationHandle(button.AsOperationMouseButtonPressed(), button);
			}

			case RdpOperationKind.MouseUp:
			{
				MouseButton button = MouseButton.New(ToButton(operation.A));
				return new RdpOperationHandle(button.AsOperationMouseButtonReleased(), button);
			}

			case RdpOperationKind.Wheel:
			{
				WheelRotations wheel = WheelRotations.New(operation.A != 0, (short)Math.Clamp(operation.B, short.MinValue, short.MaxValue));
				return new RdpOperationHandle(wheel.AsOperation(), wheel);
			}

			case RdpOperationKind.KeyDown:
			{
				Scancode scancode = Scancode.FromU16(ToUInt16(operation.A));
				return new RdpOperationHandle(scancode.AsOperationKeyPressed(), scancode);
			}

			case RdpOperationKind.KeyUp:
			{
				Scancode scancode = Scancode.FromU16(ToUInt16(operation.A));
				return new RdpOperationHandle(scancode.AsOperationKeyReleased(), scancode);
			}

			default:
				throw new ArgumentOutOfRangeException(nameof(operation), operation.Kind, "Unknown input operation.");
		}
	}

	public void Dispose()
	{
		Operation.Dispose();
		_source.Dispose();
	}

	private static ushort ToUInt16(int value) => (ushort)Math.Clamp(value, 0, ushort.MaxValue);

	private static MouseButtonType ToButton(int button) => button switch
	{
		0 => MouseButtonType.Left,
		1 => MouseButtonType.Middle,
		2 => MouseButtonType.Right,
		3 => MouseButtonType.X1,
		4 => MouseButtonType.X2,
		_ => throw new ArgumentOutOfRangeException(nameof(button), button, "RDP has five mouse buttons."),
	};
}
