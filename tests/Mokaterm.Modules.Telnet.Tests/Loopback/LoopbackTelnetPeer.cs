using System.Buffers;
using System.Net.Sockets;
using System.Text;
using Mokaterm.Modules.Telnet.Protocol;

namespace Mokaterm.Modules.Telnet.Tests.Loopback;

/// <summary>
/// The server side of one accepted connection. It parses what the client sends, so a test can wait for the next
/// negotiation or the next data bytes without writing a state machine of its own.
/// </summary>
internal sealed class LoopbackTelnetPeer : IAsyncDisposable
{
	private const int BufferSize = 4096;

	private readonly TcpClient _client;
	private readonly Stream _stream;
	private readonly TelnetInputParser _parser = new();
	private readonly Queue<TelnetMessage> _messages = new();
	private readonly List<TelnetMessage> _pending = [];
	private readonly List<byte> _data = [];
	private readonly byte[] _buffer = new byte[BufferSize];
	private readonly byte[] _parsed = new byte[BufferSize];
	private int _disposed;

	public LoopbackTelnetPeer(TcpClient client)
	{
		_client = client;
		_stream = client.GetStream();
	}

	/// <summary>Everything the client sent as data, protocol bytes already taken out.</summary>
	public IReadOnlyList<byte> Received => _data;

	public async Task SendAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
	{
		await _stream.WriteAsync(bytes, cancellationToken);
		await _stream.FlushAsync(cancellationToken);
	}

	/// <summary>Sends text as data, with IAC doubled the way a server has to.</summary>
	public Task SendTextAsync(string text, CancellationToken cancellationToken) =>
		SendTextAsync(text, Encoding.UTF8, cancellationToken);

	public Task SendTextAsync(string text, Encoding encoding, CancellationToken cancellationToken)
	{
		ArrayBufferWriter<byte> writer = new();
		TelnetWire.WriteEscaped(writer, encoding.GetBytes(text));
		return SendAsync(writer.WrittenMemory, cancellationToken);
	}

	public Task SendNegotiationAsync(byte command, byte option, CancellationToken cancellationToken)
	{
		ArrayBufferWriter<byte> writer = new();
		TelnetWire.WriteNegotiation(writer, command, option);
		return SendAsync(writer.WrittenMemory, cancellationToken);
	}

	public Task SendSubnegotiationAsync(byte option, byte[] payload, CancellationToken cancellationToken)
	{
		ArrayBufferWriter<byte> writer = new();
		TelnetWire.WriteSubnegotiation(writer, option, payload);
		return SendAsync(writer.WrittenMemory, cancellationToken);
	}

	/// <summary>Waits for the next command, negotiation or subnegotiation the client sends.</summary>
	public async Task<TelnetMessage> ReadMessageAsync(CancellationToken cancellationToken)
	{
		while (_messages.Count == 0)
		{
			await FillAsync(cancellationToken);
		}

		return _messages.Dequeue();
	}

	/// <summary>Waits for the next negotiation or subnegotiation about <paramref name="option"/>, dropping the rest.</summary>
	public async Task<TelnetMessage> ReadMessageAsync(byte option, CancellationToken cancellationToken)
	{
		while (true)
		{
			TelnetMessage message = await ReadMessageAsync(cancellationToken);
			if (message.Option == option && (message.IsNegotiation || message.IsSubnegotiation))
			{
				return message;
			}
		}
	}

	/// <summary>Waits until the client has sent at least <paramref name="count"/> data bytes and takes them.</summary>
	public async Task<byte[]> ReadDataAsync(int count, CancellationToken cancellationToken)
	{
		while (_data.Count < count)
		{
			await FillAsync(cancellationToken);
		}

		byte[] result = [.. _data.Take(count)];
		_data.RemoveRange(0, count);
		return result;
	}

	public async Task<string> ReadTextAsync(int count, CancellationToken cancellationToken) =>
		Encoding.UTF8.GetString(await ReadDataAsync(count, cancellationToken));

	/// <summary>Closes the connection the way a server that ends the session does.</summary>
	public void Close() => _client.Close();

	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 0)
		{
			_client.Dispose();
		}

		return ValueTask.CompletedTask;
	}

	private async Task FillAsync(CancellationToken cancellationToken)
	{
		int read = await _stream.ReadAsync(_buffer, cancellationToken);
		if (read == 0)
		{
			throw new EndOfStreamException("The client closed the connection.");
		}

		_pending.Clear();
		int length = _parser.Feed(_buffer.AsSpan(0, read), _parsed, _pending);
		foreach (TelnetMessage message in _pending)
		{
			_messages.Enqueue(message);
		}

		_data.AddRange(_parsed.AsSpan(0, length));
	}
}
