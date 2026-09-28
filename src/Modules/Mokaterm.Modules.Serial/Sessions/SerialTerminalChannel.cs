using System.Buffers;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Serial.Protocol;

namespace Mokaterm.Modules.Serial.Sessions;

/// <summary>
/// A terminal over a serial port. One loop reads the port and hands what arrives to the view untouched: a serial
/// line carries bytes and nothing else, so there is no framing to strip and no option to negotiate. Input goes out
/// with the line ending the connection uses and, for a device that echoes nothing, is drawn locally as well.
/// </summary>
/// <remarks>
/// The channel also answers <see cref="ISerialPortFeature"/> for the session's serial panel, because both things it
/// offers belong here: a break has to take the write lock so it cannot cut a half written line, and the loop that
/// reads the pins for the panel is the same one that notices the adapter being unplugged.
/// </remarks>
internal sealed class SerialTerminalChannel : ITerminalChannel, ISerialPortFeature
{
	private const int ReadBufferSize = 16 * 1024;

	/// <summary>A read that ended in a timeout rather than in data.</summary>
	private const int Quiet = -1;

	private readonly ISerialLink _link;
	private readonly SerialChannelOptions _options;
	private readonly SerialOutputEncoder _outputEncoder;
	private readonly TerminalTextCodec _codec;
	private readonly ArrayBufferWriter<byte> _encodeBuffer = new(1024);
	private readonly ArrayBufferWriter<byte> _transcodeBuffer = new(1024);
	private readonly TerminalOutputQueue _output = new();
	private readonly TerminalWriteLock _writeLock = new();

	// Not disposed: a read or a write that is still unwinding after the channel closed may use its token, and it
	// holds no unmanaged handle.
	private readonly CancellationTokenSource _lifetime = new();

	private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly ILogger _logger;

	private volatile SerialSignals _signals = SerialSignals.Unknown;
	private volatile bool _closed;
	private int _writeTimeouts;
	private int _disposed;

	public SerialTerminalChannel(ISerialLink link, SerialChannelOptions options, ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(link);
		ArgumentNullException.ThrowIfNull(options);
		_link = link;
		_options = options;
		_outputEncoder = new SerialOutputEncoder(options.LineEnding);
		_codec = new TerminalTextCodec(options.Encoding);
		_logger = logger;
	}

	public event Action? Changed;

	/// <summary>Completes once no more output will arrive: the port closed, the device went away or this was disposed.</summary>
	public Task Ended => _ended.Task;

	/// <summary>The reason the line ended, or null after a clean close.</summary>
	public Exception? Failure { get; private set; }

	public SerialPortStatus Status => new()
	{
		PortName = _link.PortName,
		Line = _link.Line,
		Signals = _signals,
		IsOpen = !_closed && Volatile.Read(ref _disposed) == 0,
		CanSetRts = _link.CanSetRts,
		ReportsPins = _link.ReportsPins,
		LocalEcho = _options.LocalEcho,
		LineEnding = _options.LineEnding,
		EncodingName = _codec.Encoding.WebName,
		WriteTimeouts = Volatile.Read(ref _writeTimeouts),
	};

	/// <summary>Reads the pins once and starts the loops. Called by the provider before the session is handed over.</summary>
	public void Start()
	{
		TryReadSignals();
		_ = Task.Run(() => ReadLoopAsync(_lifetime.Token), CancellationToken.None);
		if (_options.DeviceCheck > TimeSpan.Zero)
		{
			_ = Task.Run(() => DeviceCheckLoopAsync(_lifetime.Token), CancellationToken.None);
		}
	}

	public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
		_output.ReadAsync(buffer, cancellationToken);

	public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
	{
		if (data.IsEmpty || Volatile.Read(ref _disposed) != 0)
		{
			return;
		}

		if (!await TryEnterWriteAsync(cancellationToken))
		{
			return;
		}

		try
		{
			_encodeBuffer.ResetWrittenCount();
			if (_codec.IsPassthrough)
			{
				_outputEncoder.Write(data.Span, _encodeBuffer);
			}
			else
			{
				_transcodeBuffer.ResetWrittenCount();
				_codec.FromTerminal(data.Span, _transcodeBuffer);
				_outputEncoder.Write(_transcodeBuffer.WrittenSpan, _encodeBuffer);
			}

			await WriteToPortAsync(_encodeBuffer.WrittenMemory, cancellationToken);
		}
		finally
		{
			_writeLock.Release();
		}

		if (_options.LocalEcho)
		{
			await EchoAsync(data, cancellationToken);
		}
	}

	/// <summary>
	/// Does nothing: a serial line carries no window size. What the device thinks the terminal is comes from its own
	/// side (<c>stty rows cols</c>, or the program's built-in guess), so there is nothing to tell it.
	/// </summary>
	public ValueTask ResizeAsync(TerminalSize size, CancellationToken cancellationToken) => ValueTask.CompletedTask;

	public async Task SetDtrAsync(bool on, CancellationToken cancellationToken = default)
	{
		await RunPortActionAsync(() => _link.SetDtr(on), cancellationToken);
	}

	public async Task SetRtsAsync(bool on, CancellationToken cancellationToken = default)
	{
		await RunPortActionAsync(() => _link.SetRts(on), cancellationToken);
	}

	public async Task SendBreakAsync(CancellationToken cancellationToken = default)
	{
		// Under the write lock, so a break cannot land in the middle of a line that is being written.
		if (!await TryEnterWriteAsync(cancellationToken))
		{
			return;
		}

		try
		{
			_link.SetBreak(true);
			TryReadSignals();
			try
			{
				await Task.Delay(_options.Break, _options.TimeProvider, cancellationToken);
			}
			finally
			{
				_link.SetBreak(false);
			}
		}
		finally
		{
			_writeLock.Release();
			TryReadSignals();
		}
	}

	public Task RefreshAsync(CancellationToken cancellationToken = default)
	{
		TryReadSignals();
		return Task.CompletedTask;
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		_lifetime.Cancel();
		_output.Complete();
		_ended.TrySetResult();

		// A read that is already pending inside the driver only ends when the port is closed: the token it was
		// given is checked before the read starts and never again.
		await _link.DisposeAsync();
	}

	private async Task ReadLoopAsync(CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[ReadBufferSize];
		ArrayBufferWriter<byte> terminal = new(ReadBufferSize);
		try
		{
			while (true)
			{
				int read = await ReadOnceAsync(buffer, cancellationToken);
				if (read == Quiet)
				{
					continue;
				}

				if (read == 0)
				{
					return;
				}

				if (Decode(buffer, read, terminal) is { } chunk)
				{
					await _output.WriteAsync(chunk, cancellationToken);
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Disposed.
		}
		catch (Exception ex) when (IsPortFailure(ex) || ex is ChannelClosedException)
		{
			Failure ??= ex;
			_logger.LogDebug(ex, "The serial line ended.");
		}
		finally
		{
			_closed = true;
			_output.Complete();
			_ended.TrySetResult();
			Raise();
		}
	}

	/// <summary>A read, with a timeout reported as <see cref="Quiet"/>: a device that says nothing is not an error.</summary>
	private async ValueTask<int> ReadOnceAsync(byte[] buffer, CancellationToken cancellationToken)
	{
		try
		{
			return await _link.Stream.ReadAsync(buffer, cancellationToken);
		}
		catch (TimeoutException)
		{
			return Quiet;
		}
	}

	/// <summary>Applies the character set and returns what the view should see, or null for nothing.</summary>
	private byte[]? Decode(byte[] buffer, int length, ArrayBufferWriter<byte> terminal)
	{
		if (_codec.IsPassthrough)
		{
			return buffer.AsSpan(0, length).ToArray();
		}

		terminal.ResetWrittenCount();
		_codec.ToTerminal(buffer.AsSpan(0, length), terminal);
		return terminal.WrittenCount == 0 ? null : terminal.WrittenSpan.ToArray();
	}

	/// <summary>
	/// Reads the pins on a timer. This is what notices an adapter that was pulled out while the line was quiet: a
	/// read may sit in the driver for as long as the device stays silent, which is forever on a healthy port.
	/// </summary>
	private async Task DeviceCheckLoopAsync(CancellationToken cancellationToken)
	{
		using PeriodicTimer timer = new(_options.DeviceCheck, _options.TimeProvider);
		try
		{
			while (await timer.WaitForNextTickAsync(cancellationToken))
			{
				try
				{
					Update(_link.ReadSignals());
				}
				catch (Exception ex) when (IsPortFailure(ex))
				{
					await FailAsync(ex);
					return;
				}
			}
		}
		catch (OperationCanceledException)
		{
			// The session closed.
		}
	}

	private static bool IsPortFailure(Exception exception) =>
		exception is IOException or ObjectDisposedException or UnauthorizedAccessException or InvalidOperationException;

	/// <summary>Ends the channel because the port is gone. The session sees it through <see cref="Ended"/>.</summary>
	private async Task FailAsync(Exception exception)
	{
		if (Volatile.Read(ref _disposed) != 0)
		{
			return;
		}

		Failure ??= new SerialProtocolException($"{_link.PortName} is no longer there.", exception);
		_logger.LogInformation("The serial port {Port} stopped answering: {Reason}", _link.PortName, exception.GetType().Name);
		_closed = true;
		_output.Complete();
		_ended.TrySetResult();
		Raise();

		// Closing the port is also what lets go of a read that is still waiting inside the driver.
		await _link.DisposeAsync();
	}

	private ValueTask<bool> TryEnterWriteAsync(CancellationToken cancellationToken) =>
		Volatile.Read(ref _disposed) != 0 ? ValueTask.FromResult(false) : _writeLock.TryEnterAsync(cancellationToken);

	private async ValueTask WriteToPortAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
	{
		if (bytes.IsEmpty)
		{
			return;
		}

		try
		{
			await _link.Stream.WriteAsync(bytes, cancellationToken);
			await _link.Stream.FlushAsync(cancellationToken);
		}
		catch (TimeoutException)
		{
			// The device did not take the bytes in time: flow control is holding the line, or it stopped listening.
			// The keystroke is lost, which the panel says by counting it rather than by ending the session.
			Interlocked.Increment(ref _writeTimeouts);
			_logger.LogWarning("A write to {Port} timed out; the device is not taking data.", _link.PortName);
			Raise();
		}
		catch (Exception ex) when (IsPortFailure(ex))
		{
			// The port is gone; input has nowhere to go and the read loop or the check reports the close.
			_logger.LogDebug(ex, "A serial write failed after the line ended.");
		}
	}

	/// <summary>Puts what was typed on the screen while the device does not.</summary>
	private ValueTask EchoAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken) =>
		_output.WriteIfOpenAsync(TerminalLocalEcho.Build(data.Span), cancellationToken);

	private async Task RunPortActionAsync(Action action, CancellationToken cancellationToken)
	{
		if (!await TryEnterWriteAsync(cancellationToken))
		{
			return;
		}

		try
		{
			action();
		}
		finally
		{
			_writeLock.Release();
		}

		TryReadSignals();
	}

	private void TryReadSignals()
	{
		try
		{
			Update(_link.ReadSignals());
		}
		catch (Exception ex) when (IsPortFailure(ex))
		{
			// The next read or the device check reports the close; a panel refresh is not the place to end a session.
			_logger.LogDebug(ex, "Reading the serial pins failed.");
		}
	}

	private void Update(SerialSignals signals)
	{
		if (_signals == signals)
		{
			return;
		}

		_signals = signals;
		Raise();
	}

	private void Raise() => Changed?.Invoke();
}
