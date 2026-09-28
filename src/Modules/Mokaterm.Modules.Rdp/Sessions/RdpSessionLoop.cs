using Devolutions.IronRdp;
using Microsoft.Extensions.Logging;
using Mokaterm.Modules.Rdp.Protocol;
using Action = Devolutions.IronRdp.Action;

namespace Mokaterm.Modules.Rdp.Sessions;

/// <summary>
/// Runs one connected RDP session: reads the wire, keeps the decoded desktop, sends changed regions to whatever
/// page is attached and turns the page's events into input. Every IronRDP handle here is single threaded, so one
/// gate guards all of them; the reads and the picture compression happen outside it.
/// </summary>
internal sealed class RdpSessionLoop : IAsyncDisposable
{
	/// <summary>Browsers refuse a CSS cursor larger than this, so bigger pointers fall back to the default one.</summary>
	private const int MaxCursorSize = 128;

	private readonly RdpTransport _transport;
	private readonly RdpSettings _settings;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly Lock _dirtyGate = new();
	private readonly RdpDirtyRegions _dirty = new();
	private readonly RdpFrameEncoder _encoder;
	private readonly CancellationTokenSource _stopping = new();
	private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly ActiveStage _stage;
	private readonly InputDatabase _input;
	private readonly RdpInputTranslator _translator;
	private DecodedImage _image;
	private byte[] _pixels = [];
	private Task? _pump;
	private Task? _frames;
	private IRdpSink? _sink;
	private int _width;
	private int _height;
	private volatile bool _viewOnly;
	private int _disposed;

	public RdpSessionLoop(RdpTransport transport, RdpSettings settings, ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(transport);
		ArgumentNullException.ThrowIfNull(settings);
		_transport = transport;
		_settings = settings;
		_logger = logger;
		_width = transport.Width;
		_height = transport.Height;
		_encoder = new RdpFrameEncoder(settings.RawFrameKilobytes * 1024);
		_translator = new RdpInputTranslator(_width, _height);
		_stage = ActiveStage.New(transport.Result);
		try
		{
			_image = DecodedImage.New(PixelFormat.RgbA32, (ushort)_width, (ushort)_height);
			_input = InputDatabase.New();
		}
		catch
		{
			_image?.Dispose();
			_stage.Dispose();
			throw;
		}
	}

	/// <summary>Completes when the session ends, and faults with the reason when it drops.</summary>
	public Task Completion => _completion.Task;

	public int Width => Volatile.Read(ref _width);

	public int Height => Volatile.Read(ref _height);

	/// <summary>Starts reading the connection. The desktop is kept current whether a page is attached or not.</summary>
	public void Start()
	{
		_pump ??= Task.Run(PumpAsync, CancellationToken.None);
		_frames ??= Task.Run(SendFramesAsync, CancellationToken.None);
	}

	/// <summary>Points the frames at <paramref name="sink"/>, or at nothing when it is null.</summary>
	public void Attach(IRdpSink? sink) => Volatile.Write(ref _sink, sink);

	/// <summary>Sends the whole desktop with the next frame, which is what a page that just attached needs.</summary>
	public void Repaint() => MarkEverythingDirty();

	public async ValueTask SetViewOnlyAsync(bool viewOnly, CancellationToken cancellationToken)
	{
		if (_viewOnly == viewOnly)
		{
			return;
		}

		_viewOnly = viewOnly;
		if (viewOnly)
		{
			await ReleaseKeysAsync(cancellationToken);
		}
	}

	/// <summary>Applies what the user did. Events that arrive while the session is view only are dropped.</summary>
	public async ValueTask SendInputAsync(IReadOnlyList<RdpInputEvent> events, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(events);
		if (events.Count == 0 || Volatile.Read(ref _disposed) != 0)
		{
			return;
		}

		bool viewOnly = _viewOnly;
		using CancellationTokenSource linked = Link(cancellationToken);
		await _gate.WaitAsync(linked.Token);
		try
		{
			// A view only session still lets go of anything it was holding when the page asks it to.
			IReadOnlyList<RdpOperation> operations = viewOnly
				? events.Any(input => input.Kind == RdpInputKind.ReleaseKeys) ? _translator.ReleaseEverything() : []
				: _translator.Translate(events);

			await ApplyAsync(operations, linked.Token);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async ValueTask ReleaseKeysAsync(CancellationToken cancellationToken)
	{
		if (Volatile.Read(ref _disposed) != 0)
		{
			return;
		}

		using CancellationTokenSource linked = Link(cancellationToken);
		await _gate.WaitAsync(linked.Token);
		try
		{
			await ApplyAsync(_translator.ReleaseEverything(), linked.Token);
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <summary>
	/// Asks the server for a desktop of this size. False when the server did not offer the channel that carries
	/// resizes, which leaves the negotiated size in place.
	/// </summary>
	public async ValueTask<bool> ResizeAsync(int width, int height, CancellationToken cancellationToken)
	{
		if (Volatile.Read(ref _disposed) != 0)
		{
			return false;
		}

		int target = Math.Clamp(width, RdpConnectionOptions.MinDesktopSize, RdpConnectionOptions.MaxDesktopSize);
		int targetHeight = Math.Clamp(height, RdpConnectionOptions.MinDesktopSize, RdpConnectionOptions.MaxDesktopSize);
		if (target == Width && targetHeight == Height)
		{
			return true;
		}

		using CancellationTokenSource linked = Link(cancellationToken);
		await _gate.WaitAsync(linked.Token);
		try
		{
			using ActiveStageOutputIterator? outputs = _stage.EncodedResize((uint)target, (uint)targetHeight);
			if (outputs is null)
			{
				return false;
			}

			await HandleOutputsAsync(outputs, linked.Token);
			return true;
		}
		catch (IronRdpException ex)
		{
			_logger.LogDebug(ex, "The RDP server refused the resize to {Width}x{Height}.", target, targetHeight);
			return false;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		await _stopping.CancelAsync();
		Volatile.Write(ref _sink, null);

		// The frame reader has no cancellation of its own, so closing the connection is what ends the pump. The
		// connection result goes last, after the stage built from it has gone.
		await _transport.CloseAsync();
		await WaitForAsync(_pump);
		await WaitForAsync(_frames);

		// An input, resize or key release call may still be inside IronRDP. Each one holds the gate and gives up at
		// its next await now that the connection is closed and _stopping is cancelled, so the gate comes free
		// quickly. It is never released again: a handle freed under a native call would take the process down.
		await _gate.WaitAsync(CancellationToken.None);
		_stage.Dispose();
		_image.Dispose();
		_input.Dispose();
		await _transport.DisposeAsync();
		_gate.Dispose();
		_stopping.Dispose();
		_completion.TrySetResult();
	}

	private static async Task WaitForAsync(Task? task)
	{
		if (task is null)
		{
			return;
		}

		try
		{
			await task;
		}
		catch (Exception)
		{
			// The loops report what happened through Completion; disposing only has to wait for them to stop.
		}
	}

	private CancellationTokenSource Link(CancellationToken cancellationToken) =>
		CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);

	private async Task PumpAsync()
	{
		try
		{
			while (!_stopping.IsCancellationRequested)
			{
				(Action action, byte[] payload) = await _transport.Framed.ReadPdu();
				bool terminate;
				await _gate.WaitAsync(_stopping.Token);
				try
				{
					using (action)
					{
						using ActiveStageOutputIterator outputs = _stage.Process(_image, action, payload);
						terminate = await HandleOutputsAsync(outputs, _stopping.Token);
					}
				}
				finally
				{
					_gate.Release();
				}

				if (terminate)
				{
					break;
				}
			}

			_completion.TrySetResult();
		}
		catch (Exception ex) when (_stopping.IsCancellationRequested || ex is OperationCanceledException or ObjectDisposedException)
		{
			// Closing the connection is how this loop is stopped, so whatever the read threw on the way out is
			// the end of a session the user asked to close, not a failure.
			_completion.TrySetResult();
		}
		catch (Exception ex)
		{
			_logger.LogInformation(ex, "The RDP session stopped reading.");
			_completion.TrySetException(ex);
		}
		finally
		{
			await _stopping.CancelAsync();
		}
	}

	private async Task SendFramesAsync()
	{
		using PeriodicTimer timer = new(_settings.FrameInterval);
		try
		{
			while (await timer.WaitForNextTickAsync(_stopping.Token))
			{
				IRdpSink? sink = Volatile.Read(ref _sink);
				if (sink is null)
				{
					continue;
				}

				IReadOnlyList<RdpRegion> regions = TakeDirty();
				if (regions.Count == 0)
				{
					continue;
				}

				await SendRegionsAsync(sink, regions, _stopping.Token);
			}
		}
		catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
		{
			// The session is closing.
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "The RDP session stopped sending frames.");
		}
	}

	private async Task SendRegionsAsync(IRdpSink sink, IReadOnlyList<RdpRegion> regions, CancellationToken cancellationToken)
	{
		int width;
		int height;
		await _gate.WaitAsync(cancellationToken);
		try
		{
			width = _width;
			height = _height;
			CopyDesktop();
		}
		finally
		{
			_gate.Release();
		}

		foreach (RdpRegion region in regions)
		{
			RdpRegion clipped = region.Clip(width, height);
			if (clipped.IsEmpty)
			{
				continue;
			}

			// Compressing outside the gate keeps a large PNG from holding up the wire.
			await sink.FrameAsync(_encoder.Encode(_pixels, width, clipped), cancellationToken);
		}
	}

	/// <summary>Copies the decoded desktop out of IronRDP. Only call this while holding the gate.</summary>
	private void CopyDesktop()
	{
		using BytesSlice data = _image.GetData();
		int size = (int)data.GetSize();
		if (_pixels.Length != size)
		{
			// Fill refuses any buffer that is not exactly the picture's size.
			_pixels = new byte[size];
		}

		data.Fill(_pixels);
	}

	private async Task<bool> HandleOutputsAsync(ActiveStageOutputIterator outputs, CancellationToken cancellationToken)
	{
		bool terminate = false;
		while (true)
		{
			ActiveStageOutput? output = outputs.Next();
			if (output is null)
			{
				return terminate;
			}

			using (output)
			{
				switch (output.GetEnumType())
				{
					case ActiveStageOutputType.ResponseFrame:
						await WriteAsync(output, cancellationToken);
						break;
					case ActiveStageOutputType.GraphicsUpdate:
						AddGraphicsUpdate(output);
						break;
					case ActiveStageOutputType.PointerBitmap:
						await SendPointerAsync(output, cancellationToken);
						break;
					case ActiveStageOutputType.PointerPosition:
						await SendPointerPositionAsync(output, cancellationToken);
						break;
					case ActiveStageOutputType.PointerHidden:
						await SendPointerStyleAsync(hidden: true, cancellationToken);
						break;
					case ActiveStageOutputType.PointerDefault:
						await SendPointerStyleAsync(hidden: false, cancellationToken);
						break;
					case ActiveStageOutputType.Terminate:
						using (output.GetTerminate())
						{
							terminate = true;
						}

						break;
					case ActiveStageOutputType.DeactivateAll:
						await ReactivateAsync(output.GetDeactivateAll(), cancellationToken);
						break;
					default:
						break;
				}
			}
		}
	}

	private async ValueTask WriteAsync(ActiveStageOutput output, CancellationToken cancellationToken)
	{
		using BytesSlice frame = output.GetResponseFrame();
		byte[] bytes = new byte[(int)frame.GetSize()];
		frame.Fill(bytes);
		await WriteAsync(bytes, cancellationToken);
	}

	private async ValueTask WriteAsync(byte[] bytes, CancellationToken cancellationToken)
	{
		if (bytes.Length == 0)
		{
			return;
		}

		// Everything that writes holds the gate, which is what keeps two writers off one TLS stream.
		await _transport.Stream.WriteAsync(bytes, cancellationToken);
		await _transport.Stream.FlushAsync(cancellationToken);
	}

	private void AddGraphicsUpdate(ActiveStageOutput output)
	{
		using InclusiveRectangle rectangle = output.GetGraphicsUpdate();
		RdpRegion region = new(rectangle.GetLeft(), rectangle.GetTop(), rectangle.GetWidth(), rectangle.GetHeight());
		lock (_dirtyGate)
		{
			_dirty.Add(region.Clip(_width, _height));
		}
	}

	private async ValueTask SendPointerAsync(ActiveStageOutput output, CancellationToken cancellationToken)
	{
		IRdpSink? sink = Volatile.Read(ref _sink);
		if (sink is null)
		{
			return;
		}

		using DecodedPointer pointer = output.GetPointerBitmap();
		int width = pointer.GetWidth();
		int height = pointer.GetHeight();
		if (width is <= 0 or > MaxCursorSize || height is <= 0 or > MaxCursorSize)
		{
			await sink.PointerStyleAsync(hidden: false, cancellationToken);
			return;
		}

		using BytesSlice data = pointer.GetData();
		int size = (int)data.GetSize();
		if (size != width * height * PngWriter.BytesPerPixel)
		{
			_logger.LogDebug("Skipped a {Width}x{Height} pointer with {Size} bytes, which is not RGBA.", width, height, size);
			return;
		}

		byte[] pixels = new byte[size];
		data.Fill(pixels);
		RdpPointerImage image = new(PngWriter.Encode(pixels, width, height), width, height, pointer.GetHotspotX(), pointer.GetHotspotY());
		await sink.PointerImageAsync(image, cancellationToken);
	}

	private async ValueTask SendPointerPositionAsync(ActiveStageOutput output, CancellationToken cancellationToken)
	{
		Position position = output.GetPointerPosition();
		_translator.PointerMovedByServer(position.X, position.Y);
		IRdpSink? sink = Volatile.Read(ref _sink);
		if (sink is not null)
		{
			await sink.PointerMovedAsync(position.X, position.Y, cancellationToken);
		}
	}

	private async ValueTask SendPointerStyleAsync(bool hidden, CancellationToken cancellationToken)
	{
		IRdpSink? sink = Volatile.Read(ref _sink);
		if (sink is not null)
		{
			await sink.PointerStyleAsync(hidden, cancellationToken);
		}
	}

	private async ValueTask ApplyAsync(IReadOnlyList<RdpOperation> operations, CancellationToken cancellationToken)
	{
		foreach (RdpOperation operation in operations)
		{
			using RdpOperationHandle handle = RdpOperationHandle.Create(operation);
			using FastPathInputEventIterator events = _input.Apply(handle.Operation);
			using ActiveStageOutputIterator outputs = _stage.ProcessFastpathInput(_image, events);
			await HandleOutputsAsync(outputs, cancellationToken);
		}
	}

	/// <summary>
	/// The server tore the session down to build it again, which is what a resize looks like on the wire. The
	/// activation sequence runs to its end and the decoded desktop is replaced with one of the new size.
	/// </summary>
	private async ValueTask ReactivateAsync(ConnectionActivationSequence sequence, CancellationToken cancellationToken)
	{
		using (sequence)
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				using ConnectionActivationState state = sequence.GetState();
				if (state.Type == ConnectionActivationStateType.Finalized)
				{
					using ConnectionActivationStateFinalized finalized = state.GetFinalized();
					using DesktopSize size = finalized.GetDesktopSize();
					_stage.SetFastpathProcessor(
						finalized.GetIoChannelId(),
						finalized.GetUserChannelId(),
						finalized.GetEnableServerPointer(),
						finalized.GetPointerSoftwareRendering());
					await ResetDesktopAsync(size.GetWidth(), size.GetHeight(), cancellationToken);
					return;
				}

				await StepActivationAsync(sequence, cancellationToken);
			}
		}
	}

	private async ValueTask StepActivationAsync(ConnectionActivationSequence sequence, CancellationToken cancellationToken)
	{
		byte[]? input = null;
		PduHint? hint = sequence.NextPduHint();
		if (hint is not null)
		{
			using (hint)
			{
				input = await _transport.Framed.ReadByHint(hint);
			}
		}

		using WriteBuf buffer = WriteBuf.New();
		using Written written = input is null ? sequence.StepNoInput(buffer) : sequence.Step(input, buffer);
		if (written.GetWrittenType() != WrittenType.Size)
		{
			return;
		}

		using VecU8 filled = buffer.GetFilled();
		byte[] bytes = new byte[(int)filled.GetSize()];
		filled.Fill(bytes);
		await WriteAsync(bytes, cancellationToken);
	}

	private async ValueTask ResetDesktopAsync(int width, int height, CancellationToken cancellationToken)
	{
		if (!RdpDesktopBounds.IsSupported(width, height))
		{
			throw new InvalidDataException(RdpDesktopBounds.DescribeRefusal(width, height));
		}

		DecodedImage replacement = DecodedImage.New(PixelFormat.RgbA32, (ushort)width, (ushort)height);
		DecodedImage previous = _image;
		_image = replacement;
		previous.Dispose();
		Volatile.Write(ref _width, width);
		Volatile.Write(ref _height, height);
		_translator.Resize(width, height);
		MarkEverythingDirty();

		IRdpSink? sink = Volatile.Read(ref _sink);
		if (sink is not null)
		{
			await sink.DesktopResizedAsync(width, height, cancellationToken);
		}
	}

	private void MarkEverythingDirty()
	{
		lock (_dirtyGate)
		{
			_dirty.Clear();
			_dirty.Add(new RdpRegion(0, 0, Width, Height));
		}
	}

	private IReadOnlyList<RdpRegion> TakeDirty()
	{
		lock (_dirtyGate)
		{
			return _dirty.IsEmpty ? [] : _dirty.Take();
		}
	}
}
