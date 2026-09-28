using System.Text;
using System.Threading.Channels;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Sessions.Tests.Fakes;

/// <summary>A terminal channel whose output the test feeds and whose input and resizes it records.</summary>
internal sealed class FakeTerminalChannel : ITerminalChannel
{
	private readonly Channel<byte[]> _output = Channel.CreateUnbounded<byte[]>();
	private readonly Lock _lock = new();
	private readonly List<byte> _input = [];
	private readonly List<TerminalSize> _resizes = [];
	private byte[] _pending = [];
	private int _pendingOffset;
	private long _bytesRead;

	/// <summary>Every read fills the whole buffer at once and never ends, like <c>cat /dev/zero</c>.</summary>
	public bool Endless { get; init; }

	public long BytesRead => Interlocked.Read(ref _bytesRead);

	public bool IsDisposed { get; private set; }

	public string InputText
	{
		get
		{
			lock (_lock)
			{
				return Encoding.UTF8.GetString([.. _input]);
			}
		}
	}

	public IReadOnlyList<TerminalSize> Resizes
	{
		get
		{
			lock (_lock)
			{
				return [.. _resizes];
			}
		}
	}

	public void Emit(string text) => Emit(Encoding.UTF8.GetBytes(text));

	public void Emit(byte[] data) => _ = _output.Writer.TryWrite(data);

	/// <summary>Ends the output: the pending or next read returns 0.</summary>
	public void Close() => _ = _output.Writer.TryComplete();

	public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
	{
		if (Endless)
		{
			await Task.Yield();
			cancellationToken.ThrowIfCancellationRequested();
			buffer.Span.Fill((byte)'x');
			_ = Interlocked.Add(ref _bytesRead, buffer.Length);
			return buffer.Length;
		}

		if (_pendingOffset >= _pending.Length)
		{
			if (!await _output.Reader.WaitToReadAsync(cancellationToken) || !_output.Reader.TryRead(out byte[]? next))
			{
				return 0;
			}

			_pending = next;
			_pendingOffset = 0;
		}

		int count = Math.Min(buffer.Length, _pending.Length - _pendingOffset);
		_pending.AsSpan(_pendingOffset, count).CopyTo(buffer.Span);
		_pendingOffset += count;
		_ = Interlocked.Add(ref _bytesRead, count);
		return count;
	}

	public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
	{
		lock (_lock)
		{
			_input.AddRange(data.Span);
		}

		return ValueTask.CompletedTask;
	}

	public ValueTask ResizeAsync(TerminalSize size, CancellationToken cancellationToken)
	{
		lock (_lock)
		{
			_resizes.Add(size);
		}

		return ValueTask.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		IsDisposed = true;
		Close();
		return ValueTask.CompletedTask;
	}
}
