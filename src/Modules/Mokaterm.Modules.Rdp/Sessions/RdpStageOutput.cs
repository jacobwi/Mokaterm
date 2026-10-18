using Mokaterm.Modules.Rdp.Protocol;

namespace Mokaterm.Modules.Rdp.Sessions;

/// <summary>
/// One thing IronRDP asked for, already copied out of its native handle, so the loop can act on it after the handle is
/// gone and a test can make one without a connection.
/// </summary>
internal sealed class RdpStageOutput
{
	private RdpStageOutput(RdpStageOutputKind kind) => Kind = kind;

	public static RdpStageOutput Hidden { get; } = new(RdpStageOutputKind.PointerHidden);

	public static RdpStageOutput DefaultPointer { get; } = new(RdpStageOutputKind.PointerDefault);

	public static RdpStageOutput Terminate { get; } = new(RdpStageOutputKind.Terminate);

	public RdpStageOutputKind Kind { get; }

	/// <summary>The bytes of a <see cref="RdpStageOutputKind.ResponseFrame"/>.</summary>
	public byte[] Frame { get; private init; } = [];

	/// <summary>The part a <see cref="RdpStageOutputKind.GraphicsUpdate"/> redrew.</summary>
	public RdpRegion Region { get; private init; }

	/// <summary>The picture of a <see cref="RdpStageOutputKind.PointerBitmap"/>.</summary>
	public RdpPointerPixels? Pointer { get; private init; }

	/// <summary>Where a <see cref="RdpStageOutputKind.PointerPosition"/> put the pointer.</summary>
	public int X { get; private init; }

	public int Y { get; private init; }

	/// <summary>
	/// The activation a <see cref="RdpStageOutputKind.DeactivateAll"/> has to run. The loop owns it and disposes it, and
	/// whoever drops an output list early disposes the ones left (<see cref="DisposeFrom"/>).
	/// </summary>
	public IRdpActivation? Activation { get; private init; }

	public static RdpStageOutput Response(byte[] frame) => new(RdpStageOutputKind.ResponseFrame) { Frame = frame };

	public static RdpStageOutput Update(RdpRegion region) => new(RdpStageOutputKind.GraphicsUpdate) { Region = region };

	public static RdpStageOutput PointerImage(RdpPointerPixels pointer) => new(RdpStageOutputKind.PointerBitmap) { Pointer = pointer };

	public static RdpStageOutput PointerMoved(int x, int y) => new(RdpStageOutputKind.PointerPosition) { X = x, Y = y };

	public static RdpStageOutput Deactivate(IRdpActivation activation) => new(RdpStageOutputKind.DeactivateAll) { Activation = activation };

	/// <summary>Releases the activations of every output from <paramref name="start"/> on, for a list that will not be handled.</summary>
	public static void DisposeFrom(IReadOnlyList<RdpStageOutput> outputs, int start)
	{
		ArgumentNullException.ThrowIfNull(outputs);
		for (int i = Math.Max(0, start); i < outputs.Count; i++)
		{
			outputs[i].Activation?.Dispose();
		}
	}
}
