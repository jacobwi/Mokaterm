using Devolutions.IronRdp;
using Mokaterm.Modules.Rdp.Protocol;

namespace Mokaterm.Modules.Rdp.Sessions;

/// <summary>IronRDP's <c>ConnectionActivationSequence</c> for one reactivation, handed out with a Deactivate All.</summary>
internal sealed class IronRdpActivation : IRdpActivation
{
	private readonly ConnectionActivationSequence _sequence;
	private readonly IronRdpStage _stage;

	public IronRdpActivation(ConnectionActivationSequence sequence, IronRdpStage stage)
	{
		_sequence = sequence;
		_stage = stage;
	}

	public bool TryFinish(out int width, out int height)
	{
		// The state is a view of the sequence, so it goes before the next step changes the sequence.
		using ConnectionActivationState state = _sequence.GetState();
		if (state.Type != ConnectionActivationStateType.Finalized)
		{
			width = 0;
			height = 0;
			return false;
		}

		using ConnectionActivationStateFinalized finalized = state.GetFinalized();
		using DesktopSize size = finalized.GetDesktopSize();
		_stage.SetFastpathProcessor(
			finalized.GetIoChannelId(),
			finalized.GetUserChannelId(),
			finalized.GetEnableServerPointer(),
			finalized.GetPointerSoftwareRendering());
		width = size.GetWidth();
		height = size.GetHeight();
		return true;
	}

	public RdpMessageSize? NextInputSize()
	{
		PduHint? hint = _sequence.NextPduHint();
		return hint is null ? null : RdpMessageSize.Of(hint);
	}

	public byte[] Step(byte[]? input)
	{
		using WriteBuf buffer = WriteBuf.New();
		using Written written = input is null ? _sequence.StepNoInput(buffer) : _sequence.Step(input, buffer);
		if (written.GetWrittenType() != WrittenType.Size)
		{
			return [];
		}

		using VecU8 filled = buffer.GetFilled();
		byte[] bytes = new byte[(int)filled.GetSize()];
		filled.Fill(bytes);
		return bytes;
	}

	public void Dispose() => _sequence.Dispose();
}
