using System.Buffers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Modules.Telnet.Protocol;

namespace Mokaterm.Modules.Telnet.Sessions;

/// <summary>
/// A terminal over telnet. One background loop reads the socket, splits protocol messages from terminal data and
/// answers the negotiation; everything the user types goes out through <see cref="WriteAsync"/> with the line
/// ending the connection uses and every IAC doubled.
/// </summary>
internal sealed class TelnetTerminalChannel : ITerminalChannel
{
	private const int ReadBufferSize = 32 * 1024;

	private readonly Stream _stream;
	private readonly TelnetChannelOptions _options;
	private readonly TelnetNegotiator _negotiator;
	private readonly TelnetInputParser _parser = new();
	private readonly TelnetInputFilter _filter = new();
	private readonly TelnetOutputEncoder _outputEncoder;
	private readonly TerminalTextCodec _codec;
	private readonly ArrayBufferWriter<byte> _encodeBuffer = new(1024);
	private readonly ArrayBufferWriter<byte> _transcodeBuffer = new(1024);
	private readonly TerminalOutputQueue _output = new();
	private readonly TerminalWriteLock _writeLock = new();

	// Not disposed: a read or a write that is still unwinding after the channel closed may use its token, and it
	// holds no unmanaged handle.
	private readonly CancellationTokenSource _lifetime = new();

	private readonly TaskCompletionSource _negotiationSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly Lock _gate = new();

	// The read loop feeds the automatic login while the user types and the session closes on other threads.
	private readonly Lock _autoLoginGate = new();
	private readonly ILogger _logger;

	private TelnetAutoLogin? _autoLogin;
	private Decoder? _autoLoginDecoder;
	private long _startedAt;
	private TerminalSize _size;
	private volatile bool _remoteEcho;
	private volatile bool _remoteBinary;
	private volatile bool _windowSizeAgreed;
	private int _disposed;

	public TelnetTerminalChannel(Stream stream, TelnetChannelOptions options, ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(stream);
		ArgumentNullException.ThrowIfNull(options);
		_stream = stream;
		_options = options;
		_negotiator = new TelnetNegotiator(options.TerminalTypes);
		_outputEncoder = new TelnetOutputEncoder(options.LineEnding);
		_codec = new TerminalTextCodec(options.Encoding);
		_autoLogin = options.AutoLogin;
		_autoLoginDecoder = options.AutoLogin is null ? null : Encoding.UTF8.GetDecoder();
		_size = options.Size.IsValid ? options.Size : TerminalSize.Default;
		_logger = logger;
	}

	/// <summary>Completes once no more output will arrive: the server closed, the connection dropped or this was disposed.</summary>
	public Task Ended => _ended.Task;

	/// <summary>The reason the connection ended, or null after a clean close.</summary>
	public Exception? Failure { get; private set; }

	/// <summary>The terminal type the server was told about, for the session's status line.</summary>
	public string TerminalType => _negotiator.TerminalType;

	/// <summary>Sends the opening negotiation and starts reading.</summary>
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		_startedAt = _options.TimeProvider.GetTimestamp();
		ArrayBufferWriter<byte> opening = new(64);
		_negotiator.WriteInitialRequests(opening);
		await SendRawAsync(opening.WrittenMemory, cancellationToken);
		_ = Task.Run(() => ReadLoopAsync(_lifetime.Token), CancellationToken.None);
		if (_options.KeepAlive > TimeSpan.Zero)
		{
			_ = Task.Run(() => KeepAliveLoopAsync(_lifetime.Token), CancellationToken.None);
		}
	}

	/// <summary>
	/// Waits until every option asked for at the start has been answered. False when the server was still quiet
	/// after <paramref name="timeout"/>, which is normal for devices that negotiate nothing at all.
	/// </summary>
	public async Task<bool> WaitForNegotiationAsync(TimeSpan timeout, CancellationToken cancellationToken)
	{
		try
		{
			await _negotiationSettled.Task.WaitAsync(timeout, _options.TimeProvider, cancellationToken);
			return true;
		}
		catch (TimeoutException)
		{
			return false;
		}
	}

	public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
		_output.ReadAsync(buffer, cancellationToken);

	public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
	{
		NoteUserInput(data.Span);
		return WriteInputAsync(data, echo: true, cancellationToken);
	}

	public async ValueTask ResizeAsync(TerminalSize size, CancellationToken cancellationToken)
	{
		if (!size.IsValid || Volatile.Read(ref _disposed) != 0)
		{
			return;
		}

		lock (_gate)
		{
			if (_size == size)
			{
				return;
			}

			_size = size;
		}

		// A server that refused NAWS learns about the size only if it asks for the option later.
		if (_windowSizeAgreed)
		{
			await SendWindowSizeAsync(cancellationToken);
		}
	}

	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 0)
		{
			_lifetime.Cancel();
			_output.Complete();
			_ended.TrySetResult();
			_negotiationSettled.TrySetResult();
			DropAutoLogin();
		}

		return ValueTask.CompletedTask;
	}

	private bool ShouldEchoLocally() => _options.Echo switch
	{
		TelnetEchoMode.On => true,
		TelnetEchoMode.Off => false,
		_ => !_remoteEcho,
	};

	private async Task ReadLoopAsync(CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[ReadBufferSize];
		byte[] data = new byte[ReadBufferSize];
		List<TelnetMessage> messages = [];
		ArrayBufferWriter<byte> replies = new(64);
		ArrayBufferWriter<byte> terminal = new(ReadBufferSize);
		try
		{
			while (true)
			{
				int read = await _stream.ReadAsync(buffer, cancellationToken);
				if (read == 0)
				{
					return;
				}

				messages.Clear();
				int length = _parser.Feed(buffer.AsSpan(0, read), data, messages);
				if (messages.Count > 0)
				{
					await AnswerAsync(messages, replies, cancellationToken);
				}

				if (Decode(data, length, terminal) is { } chunk)
				{
					await _output.WriteAsync(chunk, cancellationToken);
					await RunAutoLoginAsync(chunk, cancellationToken);
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Disposed.
		}
		catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or ChannelClosedException)
		{
			Failure = ex;
			_logger.LogDebug(ex, "The telnet connection ended.");
		}
		finally
		{
			_output.Complete();
			_negotiationSettled.TrySetResult();
			_ended.TrySetResult();
		}
	}

	private async Task AnswerAsync(List<TelnetMessage> messages, ArrayBufferWriter<byte> replies, CancellationToken cancellationToken)
	{
		replies.ResetWrittenCount();
		bool windowSizeWasAgreed = _windowSizeAgreed;
		foreach (TelnetMessage message in messages)
		{
			_logger.LogTrace("Telnet received {Message}", message);
			_negotiator.Handle(message, replies);
		}

		_remoteEcho = _negotiator.IsRemoteEnabled(TelnetOption.Echo);
		_remoteBinary = _negotiator.IsRemoteEnabled(TelnetOption.Binary);
		_windowSizeAgreed = _negotiator.IsLocalEnabled(TelnetOption.NegotiateAboutWindowSize);
		if (replies.WrittenCount > 0)
		{
			await SendRawAsync(replies.WrittenMemory, cancellationToken);
		}

		// The size is the first thing a server wants after agreeing to NAWS, and it never asks for it.
		if (_windowSizeAgreed && !windowSizeWasAgreed)
		{
			await SendWindowSizeAsync(cancellationToken);
		}

		if (_negotiator.IsSettled)
		{
			_negotiationSettled.TrySetResult();
		}
	}

	/// <summary>Applies the NVT rules and the character set, and returns what the view should see, or null for nothing.</summary>
	private byte[]? Decode(byte[] data, int length, ArrayBufferWriter<byte> terminal)
	{
		int filtered = _filter.Filter(data.AsSpan(0, length), _remoteBinary);
		if (filtered == 0)
		{
			return null;
		}

		if (_codec.IsPassthrough)
		{
			return data.AsSpan(0, filtered).ToArray();
		}

		terminal.ResetWrittenCount();
		_codec.ToTerminal(data.AsSpan(0, filtered), terminal);
		return terminal.WrittenCount == 0 ? null : terminal.WrittenSpan.ToArray();
	}

	/// <summary>Watches the output for a login prompt and types the saved answer. Never logs what it sends.</summary>
	private async Task RunAutoLoginAsync(byte[] chunk, CancellationToken cancellationToken)
	{
		TelnetAutoLoginStep? step;
		lock (_autoLoginGate)
		{
			if (_autoLogin is not { IsFinished: false } login || _autoLoginDecoder is not { } decoder)
			{
				return;
			}

			if (_options.TimeProvider.GetElapsedTime(_startedAt) > _options.AutoLoginTimeout)
			{
				_logger.LogInformation("No login prompt showed up in time; the automatic login was dropped.");
				DropAutoLoginLocked();
				return;
			}

			char[] chars = ArrayPool<char>.Shared.Rent(Encoding.UTF8.GetMaxCharCount(chunk.Length));
			try
			{
				int count = decoder.GetChars(chunk, 0, chunk.Length, chars, 0, flush: false);
				step = login.Feed(chars.AsSpan(0, count));
			}
			finally
			{
				ArrayPool<char>.Shared.Return(chars);
			}

			// Typed in full, or stopped because the prompts went somewhere else: either way there is nothing left to do.
			if (login.IsFinished)
			{
				DropAutoLoginLocked();
			}
		}

		if (step is not { } answer)
		{
			return;
		}

		// The line ending is added here so the answer goes out exactly as if it had been typed.
		byte[] input = new byte[answer.Input.Length + 1];
		try
		{
			answer.Input.CopyTo(input, 0);
			input[^1] = (byte)'\r';
			await WriteInputAsync(input, answer.Echo, cancellationToken);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(input);
			CryptographicOperations.ZeroMemory(answer.Input);
		}
	}

	/// <summary>Enter from the user ends an automatic login that has typed the user name: the user took over.</summary>
	private void NoteUserInput(ReadOnlySpan<byte> data)
	{
		if (Volatile.Read(ref _autoLogin) is null || data.IndexOfAny((byte)'\r', (byte)'\n') < 0)
		{
			return;
		}

		lock (_autoLoginGate)
		{
			if (_autoLogin is not { } login)
			{
				return;
			}

			login.LineSubmitted();
			if (login.IsFinished)
			{
				_logger.LogInformation("The automatic login stopped because the user took over before the password prompt.");
				DropAutoLoginLocked();
			}
		}
	}

	private void DropAutoLogin()
	{
		lock (_autoLoginGate)
		{
			DropAutoLoginLocked();
		}
	}

	// Call with _autoLoginGate held.
	private void DropAutoLoginLocked()
	{
		_autoLogin?.Dispose();
		_autoLogin = null;
		_autoLoginDecoder = null;
	}

	private async ValueTask WriteInputAsync(ReadOnlyMemory<byte> data, bool echo, CancellationToken cancellationToken)
	{
		if (data.IsEmpty || Volatile.Read(ref _disposed) != 0 || !await _writeLock.TryEnterAsync(cancellationToken))
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

			await WriteToStreamAsync(_encodeBuffer.WrittenMemory, cancellationToken);
		}
		finally
		{
			_writeLock.Release();
		}

		if (echo && ShouldEchoLocally())
		{
			await EchoAsync(data, cancellationToken);
		}
	}

	/// <summary>Puts what was typed on the screen while the server does not.</summary>
	private ValueTask EchoAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken) =>
		_output.WriteIfOpenAsync(TerminalLocalEcho.Build(data.Span), cancellationToken);

	private async ValueTask SendWindowSizeAsync(CancellationToken cancellationToken)
	{
		TerminalSize size;
		lock (_gate)
		{
			size = _size;
		}

		ArrayBufferWriter<byte> writer = new(16);
		TelnetWire.WriteWindowSize(writer, size);
		await SendRawAsync(writer.WrittenMemory, cancellationToken);
	}

	/// <summary>Writes protocol bytes that are already escaped, such as a negotiation or a subnegotiation.</summary>
	private async ValueTask SendRawAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
	{
		if (bytes.IsEmpty || !await _writeLock.TryEnterAsync(cancellationToken))
		{
			return;
		}

		try
		{
			await WriteToStreamAsync(bytes, cancellationToken);
		}
		finally
		{
			_writeLock.Release();
		}
	}

	private async ValueTask WriteToStreamAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
	{
		if (bytes.IsEmpty)
		{
			return;
		}

		try
		{
			await _stream.WriteAsync(bytes, cancellationToken);
			await _stream.FlushAsync(cancellationToken);
		}
		catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
		{
			// The connection is gone; input has nowhere to go and the read loop reports the close.
			_logger.LogDebug(ex, "A telnet write failed after the connection ended.");
		}
	}

	private async Task KeepAliveLoopAsync(CancellationToken cancellationToken)
	{
		using PeriodicTimer timer = new(_options.KeepAlive, _options.TimeProvider);
		ArrayBufferWriter<byte> writer = new(2);
		TelnetWire.WriteCommand(writer, TelnetCommand.Nop);
		try
		{
			while (await timer.WaitForNextTickAsync(cancellationToken))
			{
				await SendRawAsync(writer.WrittenMemory, cancellationToken);
			}
		}
		catch (OperationCanceledException)
		{
			// The session closed.
		}
	}
}
