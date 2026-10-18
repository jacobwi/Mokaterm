using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Diagnostics;
using Mokaterm.Modules.Rdp.Protocol;
using Mokaterm.UI.Common.Interop;
using Action = Devolutions.IronRdp.Action;

namespace Mokaterm.Modules.Rdp.Sessions;

/// <summary>
/// Runs one connected RDP session: reads the wire, keeps the decoded desktop, sends changed regions to whatever
/// page is attached and turns the page's events into input. Every IronRDP handle here is single threaded, so one
/// gate guards all of them; the reads and the picture compression happen outside it.
/// </summary>
/// <remarks>
/// Nothing that holds the gate ever waits on the page. The pump and the input calls only record what the page should
/// learn (changed regions, the pointer, the desktop size), and the frame loop takes that state each tick and is the one
/// caller of the sink, so a slow page slows its own picture and holds up neither the wire nor the keyboard. Taking the
/// state rather than queueing it also coalesces: a page that falls behind gets the latest pointer, not every one.
/// </remarks>
internal sealed class RdpSessionLoop : IAsyncDisposable
{
	private readonly IRdpWire _wire;
	private readonly IRdpStage _stage;
	private readonly RdpSettings _settings;
	private readonly ILogger _logger;
	private readonly int _ioChannelId;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly CancellationTokenSource _stopping = new();
	private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly RdpInputTranslator _translator;

	// What the page should learn next, under _pageGate. Producers hold the session gate or not; it does not matter.
	private readonly Lock _pageGate = new();
	private readonly RdpDirtyRegions _dirty = new();
	private RdpPointerState? _pointer;
	private int _pointerVersion;
	private (int X, int Y) _serverPointer;
	private int _serverPointerVersion;
	private int _serverPointerAtAttach;

	// Only the frame loop touches these: what the page it last painted already has.
	private readonly RdpFrameEncoder _encoder;
	private byte[] _pixels = [];
	private IRdpSink? _painted;
	private int _paintedWidth;
	private int _paintedHeight;
	private int _paintedPointerVersion;
	private int _paintedServerPointerVersion;

	private Task? _pump;
	private Task? _frames;
	private IRdpSink? _sink;
	private int _width;
	private int _height;
	private volatile bool _viewOnly;
	private string? _closeReason;
	private int _disposed;

	/// <param name="wire">The connection. The loop closes it when the session ends, but the caller disposes it.</param>
	/// <param name="stage">The IronRDP handles, which the loop owns from here and frees in <see cref="DisposeAsync"/>.</param>
	public RdpSessionLoop(IRdpWire wire, IRdpStage stage, int width, int height, RdpSettings settings, ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(wire);
		ArgumentNullException.ThrowIfNull(stage);
		ArgumentNullException.ThrowIfNull(settings);
		_wire = wire;
		_stage = stage;
		_settings = settings;
		_logger = logger;
		_ioChannelId = stage.IoChannelId;
		_width = width;
		_height = height;
		_encoder = new RdpFrameEncoder(settings.RawFrameKilobytes * 1024);
		_translator = new RdpInputTranslator(width, height);
	}

	/// <summary>
	/// Completes when the session ends, and faults with a reason the user can read when it drops. An end the server
	/// announced completes it, with <see cref="CloseReason"/> saying why.
	/// </summary>
	public Task Completion => _completion.Task;

	/// <summary>Why the server ended the session, once it has. Null while it runs and after a drop.</summary>
	public string? CloseReason => Volatile.Read(ref _closeReason);

	public int Width => Volatile.Read(ref _width);

	public int Height => Volatile.Read(ref _height);

	/// <summary>Starts reading the connection. The desktop is kept current whether a page is attached or not.</summary>
	public void Start()
	{
		_pump ??= Task.Run(PumpAsync, CancellationToken.None);
		_frames ??= Task.Run(SendFramesAsync, CancellationToken.None);
	}

	/// <summary>
	/// Points the frames at <paramref name="sink"/>, or at nothing when it is null. A new page is told the desktop size
	/// before its first frame, and gets the pointer the server last drew.
	/// </summary>
	public void Attach(IRdpSink? sink)
	{
		lock (_pageGate)
		{
			// Where the server put the pointer before this page came is old news: the user has moved it since.
			_serverPointerAtAttach = _serverPointerVersion;
			Volatile.Write(ref _sink, sink);
		}
	}

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
			List<RdpStageOutput> outputs = [];
			if (!_stage.TryEncodeResize(target, targetHeight, outputs))
			{
				return false;
			}

			await HandleOutputsAsync(outputs, reactivate: false, linked.Token);
			return true;
		}
		catch (Devolutions.IronRdp.IronRdpException ex)
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

		// The frame reader has no cancellation of its own once IronRDP is working out a length, so closing the
		// connection is what ends the pump.
		await _wire.CloseAsync();
		await WaitForAsync(_pump);
		await WaitForAsync(_frames);

		// An input, resize or key release call may still be inside IronRDP. Each one holds the gate and gives up at
		// its next await now that the connection is closed and _stopping is cancelled, so the gate comes free
		// quickly. It is never released again: a handle freed under a native call would take the process down.
		await _gate.WaitAsync(CancellationToken.None);
		_stage.Dispose();
		_encoder.Dispose();
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
			while (true)
			{
				(Action action, byte[] payload) = await _wire.Reader.ReadPduAsync(_stopping.Token);
				RdpServerEnd? end;
				using (action)
				{
					end = RdpServerEnd.Read(payload, _ioChannelId) ?? await ProcessAsync(action, payload);
				}

				if (end is not null)
				{
					Finish(end);
					return;
				}
			}
		}
		catch (Exception ex) when (_stopping.IsCancellationRequested || ex is OperationCanceledException or ObjectDisposedException)
		{
			// Closing the connection is how this loop is stopped, so whatever the read threw on the way out is
			// the end of a session the user asked to close, not a failure.
			_completion.TrySetResult();
		}
		catch (Exception ex)
		{
			// A dropped connection is expected here, and its message can name the server.
			_logger.LogInformation("The RDP session stopped reading: {Error}", LogSafe.Describe(ex));
			_completion.TrySetException(RdpSessionErrors.Dropped(ex));
		}
		finally
		{
			await StopAsync();
		}
	}

	private async ValueTask<RdpServerEnd?> ProcessAsync(Action action, byte[] payload)
	{
		await _gate.WaitAsync(_stopping.Token);
		try
		{
			List<RdpStageOutput> outputs = [];
			_stage.Process(action, payload, outputs);
			return await HandleOutputsAsync(outputs, reactivate: true, _stopping.Token);
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <summary>
	/// Acts on what IronRDP asked for, in order. Only call this while holding the gate. Returns how the session ends
	/// when one of the outputs ends it.
	/// </summary>
	/// <param name="reactivate">
	/// False for input and resize calls, which must never run a reactivation: that reads the wire, which only the pump
	/// may do. IronRDP never answers input with one.
	/// </param>
	private async ValueTask<RdpServerEnd?> HandleOutputsAsync(List<RdpStageOutput> outputs, bool reactivate, CancellationToken cancellationToken)
	{
		RdpServerEnd? end = null;
		int next = 0;

		// The pointer changes of one message reach the page together, so a frame tick in between cannot show it a
		// pointer the same message already replaced.
		RdpPointerState? pointer = null;
		(int X, int Y)? moved = null;
		try
		{
			while (next < outputs.Count && end is null)
			{
				RdpStageOutput output = outputs[next++];
				switch (output.Kind)
				{
					case RdpStageOutputKind.ResponseFrame:
						// Everything that writes holds the gate, which is what keeps two writers off one TLS stream.
						await _wire.WriteAsync(output.Frame, cancellationToken);
						break;
					case RdpStageOutputKind.GraphicsUpdate:
						AddDirty(output.Region);
						break;
					case RdpStageOutputKind.PointerBitmap:
						pointer = new RdpPointerState(output.Pointer, Hidden: false);
						break;
					case RdpStageOutputKind.PointerHidden:
						pointer = RdpPointerState.HiddenPointer;
						break;
					case RdpStageOutputKind.PointerDefault:
						pointer = RdpPointerState.Default;
						break;
					case RdpStageOutputKind.PointerPosition:
						_translator.PointerMovedByServer(output.X, output.Y);
						moved = (output.X, output.Y);
						break;
					case RdpStageOutputKind.Terminate:
						end = RdpServerEnd.Terminated;
						break;
					case RdpStageOutputKind.DeactivateAll when output.Activation is { } activation:
						if (reactivate)
						{
							end = await ReactivateAsync(activation, cancellationToken);
						}
						else
						{
							activation.Dispose();
						}

						break;
					default:
						break;
				}
			}
		}
		finally
		{
			RdpStageOutput.DisposeFrom(outputs, next);
			if (pointer is not null || moved is not null)
			{
				SetPointer(pointer, moved);
			}
		}

		return end;
	}

	/// <summary>
	/// The server tore the session down to build it again, which is what a resize looks like on the wire, and what a
	/// login on the server's own screen does once it moves to the user's session. The activation runs to its end and
	/// the decoded desktop is replaced with one of the new size. The server can still end the session here, in which
	/// case the step it would have fed is skipped and the end comes back.
	/// </summary>
	private async ValueTask<RdpServerEnd?> ReactivateAsync(IRdpActivation activation, CancellationToken cancellationToken)
	{
		using (activation)
		{
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (activation.TryFinish(out int width, out int height))
				{
					ResetDesktop(width, height);
					return null;
				}

				byte[]? input = null;
				using (RdpMessageSize? size = activation.NextInputSize())
				{
					if (size is not null)
					{
						input = await _wire.Reader.ReadMessageAsync(size.Find, cancellationToken);
					}
				}

				if (input is not null && RdpServerEnd.Read(input, _ioChannelId) is { } end)
				{
					return end;
				}

				byte[] reply = activation.Step(input);
				if (reply.Length > 0)
				{
					await _wire.WriteAsync(reply, cancellationToken);
				}
			}
		}
	}

	/// <summary>Only call this while holding the gate.</summary>
	private void ResetDesktop(int width, int height)
	{
		if (!RdpDesktopBounds.IsSupported(width, height))
		{
			throw new RdpSessionException(RdpDesktopBounds.DescribeRefusal(width, height));
		}

		_stage.ResetDesktop(width, height);
		Volatile.Write(ref _width, width);
		Volatile.Write(ref _height, height);
		_translator.Resize(width, height);
		MarkEverythingDirty();
	}

	private async ValueTask ApplyAsync(IReadOnlyList<RdpOperation> operations, CancellationToken cancellationToken)
	{
		foreach (RdpOperation operation in operations)
		{
			List<RdpStageOutput> outputs = [];
			_stage.ApplyInput(operation, outputs);
			await HandleOutputsAsync(outputs, reactivate: false, cancellationToken);
		}
	}

	/// <summary>The server ended the session on purpose and said why.</summary>
	private void Finish(RdpServerEnd end)
	{
		_logger.LogInformation("The RDP server ended the session ({Code}).", end.Code);
		if (end.Reconnects)
		{
			_completion.TrySetException(new RdpSessionException(end.Reason));
			return;
		}

		Volatile.Write(ref _closeReason, end.Reason);
		_completion.TrySetResult();
	}

	/// <summary>Stops both loops and closes the connection. The completion must already say how the session ended.</summary>
	private async Task StopAsync()
	{
		await _stopping.CancelAsync();
		await _wire.CloseAsync();
	}

	private async Task SendFramesAsync()
	{
		using PeriodicTimer timer = new(_settings.FrameInterval);
		try
		{
			while (await timer.WaitForNextTickAsync(_stopping.Token))
			{
				if (Volatile.Read(ref _sink) is { } sink)
				{
					await PaintAsync(sink);
				}
			}
		}
		catch (Exception) when (_stopping.IsCancellationRequested)
		{
			// The session is closing.
		}
		catch (Exception ex)
		{
			// Something on this side failed to make or hand over the picture. The connection still works, but a
			// session whose picture no longer changes looks alive while it is not, so it drops instead and the tab
			// offers to reconnect.
			_logger.LogWarning("The RDP session stopped sending frames: {Error}", LogSafe.Describe(ex));
			_completion.TrySetException(RdpSessionErrors.StoppedDrawing(ex));
			await StopAsync();
		}
	}

	/// <summary>One tick: what changed since the last one goes to the page, the desktop size first.</summary>
	private async Task PaintAsync(IRdpSink sink)
	{
		if (!ReferenceEquals(sink, _painted))
		{
			// A page that just attached has nothing: it learns the size before its first frame and the pointer the
			// server last drew, and where the server moved the pointer since it attached.
			_painted = sink;
			_paintedWidth = 0;
			_paintedHeight = 0;
			_paintedPointerVersion = 0;
			lock (_pageGate)
			{
				_paintedServerPointerVersion = _serverPointerAtAttach;
			}
		}

		IReadOnlyList<RdpRegion> regions;
		RdpPointerState? pointer = null;
		(int X, int Y)? moved = null;
		lock (_pageGate)
		{
			regions = _dirty.IsEmpty ? [] : _dirty.Take();
			if (_pointerVersion != _paintedPointerVersion)
			{
				pointer = _pointer;
				_paintedPointerVersion = _pointerVersion;
			}

			if (_serverPointerVersion != _paintedServerPointerVersion)
			{
				moved = _serverPointer;
				_paintedServerPointerVersion = _serverPointerVersion;
			}
		}

		if (regions.Count > 0 && !await PaintRegionsAsync(sink, regions))
		{
			return;
		}

		if (pointer is not null && !await ToPageAsync(sink, PointerCall(pointer)))
		{
			return;
		}

		if (moved is (int x, int y))
		{
			await ToPageAsync(sink, (page, token) => page.PointerMovedAsync(x, y, token));
		}
	}

	/// <returns>False when the page went away on the way.</returns>
	private async Task<bool> PaintRegionsAsync(IRdpSink sink, IReadOnlyList<RdpRegion> regions)
	{
		int width;
		int height;
		await _gate.WaitAsync(_stopping.Token);
		try
		{
			// One copy of the desktop per tick, the only way the bindings hand the picture out (see the RDP notes);
			// every region below is cut from it outside the gate.
			width = _width;
			height = _height;
			_pixels = _stage.CopyDesktop(_pixels);
		}
		finally
		{
			_gate.Release();
		}

		if (_pixels.Length != width * height * PngWriter.BytesPerPixel)
		{
			throw new InvalidOperationException("The decoded desktop is not the size the session thinks it is.");
		}

		if ((width, height) != (_paintedWidth, _paintedHeight))
		{
			if (!await ToPageAsync(sink, (page, token) => page.DesktopResizedAsync(width, height, token)))
			{
				return false;
			}

			_paintedWidth = width;
			_paintedHeight = height;
		}

		foreach (RdpRegion region in regions)
		{
			RdpRegion clipped = region.Clip(width, height);
			if (clipped.IsEmpty)
			{
				continue;
			}

			// Compressing outside the gate keeps a large PNG from holding up the wire.
			byte[] frame = _encoder.Encode(_pixels, width, clipped);
			if (!await ToPageAsync(sink, (page, token) => page.FrameAsync(frame, token)))
			{
				return false;
			}
		}

		return true;
	}

	private static Func<IRdpSink, CancellationToken, ValueTask> PointerCall(RdpPointerState pointer)
	{
		if (pointer.Image is not { } image)
		{
			return (page, token) => page.PointerStyleAsync(pointer.Hidden, token);
		}

		RdpPointerImage cursor = new(PngWriter.Encode(image.Rgba, image.Width, image.Height), image.Width, image.Height, image.HotspotX, image.HotspotY);
		return (page, token) => page.PointerImageAsync(cursor, token);
	}

	/// <summary>
	/// One call into the page. The page going away under it (the interop teardown and script failures that
	/// <see cref="JsModule.IsExpected"/> names, which <see cref="PageCalls"/> already swallows for the real page) is an
	/// ordinary detach: this page gets nothing more, and the next one to attach is repainted. Anything else is thrown.
	/// </summary>
	/// <returns>False when the page went away.</returns>
	private async Task<bool> ToPageAsync(IRdpSink sink, Func<IRdpSink, CancellationToken, ValueTask> call)
	{
		try
		{
			await call(sink, _stopping.Token);
			return true;
		}
		catch (Exception ex) when (!_stopping.IsCancellationRequested && JsModule.IsExpected(ex))
		{
			_logger.LogDebug("The RDP page went away while it was being painted: {Error}", LogSafe.Describe(ex));
			Interlocked.CompareExchange(ref _sink, null, sink);
			return false;
		}
	}

	private void AddDirty(RdpRegion region)
	{
		lock (_pageGate)
		{
			_dirty.Add(region.Clip(Width, Height));
		}
	}

	private void SetPointer(RdpPointerState? pointer, (int X, int Y)? moved)
	{
		lock (_pageGate)
		{
			if (pointer is not null)
			{
				_pointer = pointer;
				_pointerVersion++;
			}

			if (moved is { } position)
			{
				_serverPointer = position;
				_serverPointerVersion++;
			}
		}
	}

	private void MarkEverythingDirty()
	{
		lock (_pageGate)
		{
			_dirty.Clear();
			_dirty.Add(new RdpRegion(0, 0, Width, Height));
		}
	}
}
