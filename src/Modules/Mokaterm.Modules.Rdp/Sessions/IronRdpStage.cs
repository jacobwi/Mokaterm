using Devolutions.IronRdp;
using Mokaterm.Modules.Rdp.Protocol;
using Action = Devolutions.IronRdp.Action;

namespace Mokaterm.Modules.Rdp.Sessions;

/// <summary>
/// The IronRDP half of a running session: <c>ActiveStage</c>, the <c>DecodedImage</c> it draws into and the
/// <c>InputDatabase</c> that turns operations into fast-path input.
/// </summary>
internal sealed class IronRdpStage : IRdpStage
{
	/// <summary>Browsers refuse a CSS cursor larger than this, so bigger pointers fall back to the default one.</summary>
	public const int MaxCursorSize = 128;

	private readonly ActiveStage _stage;
	private readonly InputDatabase _input;
	private DecodedImage _image;

	private IronRdpStage(ActiveStage stage, DecodedImage image, InputDatabase input, int ioChannelId)
	{
		_stage = stage;
		_image = image;
		_input = input;
		IoChannelId = ioChannelId;
	}

	public int IoChannelId { get; }

	/// <summary>
	/// Builds the stage from what the connector agreed on. The stage takes the result's contents, but the result's own
	/// handle stays the caller's and goes after this stage has.
	/// </summary>
	public static IronRdpStage Create(ConnectionResult result, int width, int height)
	{
		ArgumentNullException.ThrowIfNull(result);

		// Read before the stage takes the result over, after which every getter on it fails.
		int ioChannelId = result.GetIoChannelId();
		ActiveStage stage = ActiveStage.New(result);
		DecodedImage? image = null;
		try
		{
			image = DecodedImage.New(PixelFormat.RgbA32, (ushort)width, (ushort)height);
			return new IronRdpStage(stage, image, InputDatabase.New(), ioChannelId);
		}
		catch
		{
			image?.Dispose();
			stage.Dispose();
			throw;
		}
	}

	public void Process(Action action, byte[] payload, List<RdpStageOutput> outputs)
	{
		using ActiveStageOutputIterator iterator = _stage.Process(_image, action, payload);
		Collect(iterator.Next, Convert, outputs);
	}

	public void ApplyInput(RdpOperation operation, List<RdpStageOutput> outputs)
	{
		using RdpOperationHandle handle = RdpOperationHandle.Create(operation);
		using FastPathInputEventIterator events = _input.Apply(handle.Operation);
		using ActiveStageOutputIterator iterator = _stage.ProcessFastpathInput(_image, events);
		Collect(iterator.Next, Convert, outputs);
	}

	public bool TryEncodeResize(int width, int height, List<RdpStageOutput> outputs)
	{
		using ActiveStageOutputIterator? iterator = _stage.EncodedResize((uint)width, (uint)height);
		if (iterator is null)
		{
			return false;
		}

		Collect(iterator.Next, Convert, outputs);
		return true;
	}

	public byte[] CopyDesktop(byte[] buffer)
	{
		using BytesSlice data = _image.GetData();
		int size = (int)data.GetSize();

		// Fill refuses a shorter buffer, and a longer one fails copy_from_slice in Rust, which aborts the process.
		byte[] target = buffer.Length == size ? buffer : GC.AllocateUninitializedArray<byte>(size);
		data.Fill(target);
		return target;
	}

	public void ResetDesktop(int width, int height)
	{
		DecodedImage replacement = DecodedImage.New(PixelFormat.RgbA32, (ushort)width, (ushort)height);
		DecodedImage previous = _image;
		_image = replacement;
		previous.Dispose();
	}

	public void Dispose()
	{
		_stage.Dispose();
		_image.Dispose();
		_input.Dispose();
	}

	/// <summary>
	/// Turns what <paramref name="next"/> hands out into outputs, in the order IronRDP wants them handled. The bindings'
	/// <c>ActiveStageOutputIterator.next</c> is <c>Vec::pop</c>, which hands the list <c>ActiveStage</c> returned back last
	/// first, while the x224 processor documents its outputs as "must be processed in the returned order": a Shutdown
	/// Denied answers with the ultimatum to send and then the end, and one fast-path update can carry a pointer change
	/// after a pointer change. So the outputs are read to the end and turned around.
	/// </summary>
	/// <param name="next">The next native output, null at the end. Each one is disposed once converted.</param>
	/// <param name="convert">Copies what the loop needs out of a native output, or returns null for one it ignores.</param>
	internal static void Collect<TNative>(Func<TNative?> next, Func<TNative, RdpStageOutput?> convert, List<RdpStageOutput> outputs)
		where TNative : class, IDisposable
	{
		int start = outputs.Count;
		try
		{
			while (next() is { } native)
			{
				using (native)
				{
					if (convert(native) is { } output)
					{
						outputs.Add(output);
					}
				}
			}
		}
		catch
		{
			RdpStageOutput.DisposeFrom(outputs, start);
			outputs.RemoveRange(start, outputs.Count - start);
			throw;
		}

		outputs.Reverse(start, outputs.Count - start);
	}

	/// <summary>Points the decoder at the session the server activated again.</summary>
	internal void SetFastpathProcessor(ushort ioChannelId, ushort userChannelId, bool enableServerPointer, bool pointerSoftwareRendering) =>
		_stage.SetFastpathProcessor(ioChannelId, userChannelId, enableServerPointer, pointerSoftwareRendering);

	private static RdpStageOutput? PointerOf(ActiveStageOutput output)
	{
		using DecodedPointer pointer = output.GetPointerBitmap();
		int width = pointer.GetWidth();
		int height = pointer.GetHeight();
		if (width is <= 0 or > MaxCursorSize || height is <= 0 or > MaxCursorSize)
		{
			return RdpStageOutput.DefaultPointer;
		}

		using BytesSlice data = pointer.GetData();
		int size = (int)data.GetSize();
		if (size != width * height * PngWriter.BytesPerPixel)
		{
			// Not RGBA, so there is nothing to show; the pointer the page has stays.
			return null;
		}

		byte[] pixels = new byte[size];
		data.Fill(pixels);
		return RdpStageOutput.PointerImage(new RdpPointerPixels(pixels, width, height, pointer.GetHotspotX(), pointer.GetHotspotY()));
	}

	private RdpStageOutput? Convert(ActiveStageOutput output)
	{
		switch (output.GetEnumType())
		{
			case ActiveStageOutputType.ResponseFrame:
			{
				using BytesSlice frame = output.GetResponseFrame();
				int size = (int)frame.GetSize();
				if (size == 0)
				{
					return null;
				}

				byte[] bytes = new byte[size];
				frame.Fill(bytes);
				return RdpStageOutput.Response(bytes);
			}

			case ActiveStageOutputType.GraphicsUpdate:
			{
				using InclusiveRectangle rectangle = output.GetGraphicsUpdate();
				return RdpStageOutput.Update(new RdpRegion(rectangle.GetLeft(), rectangle.GetTop(), rectangle.GetWidth(), rectangle.GetHeight()));
			}

			case ActiveStageOutputType.PointerBitmap:
				return PointerOf(output);
			case ActiveStageOutputType.PointerPosition:
			{
				Position position = output.GetPointerPosition();
				return RdpStageOutput.PointerMoved(position.X, position.Y);
			}

			case ActiveStageOutputType.PointerHidden:
				return RdpStageOutput.Hidden;
			case ActiveStageOutputType.PointerDefault:
				return RdpStageOutput.DefaultPointer;
			case ActiveStageOutputType.Terminate:
				using (output.GetTerminate())
				{
					// The reason has no accessor in the bindings; RdpServerEnd reads the messages that carry one first.
					return RdpStageOutput.Terminate;
				}

			case ActiveStageOutputType.DeactivateAll:
				return RdpStageOutput.Deactivate(new IronRdpActivation(output.GetDeactivateAll(), this));
			default:
				return null;
		}
	}
}
