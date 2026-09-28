namespace Mokaterm.Sessions.Terminal;

/// <summary>
/// A ring holding the newest output up to a fixed number of bytes, for views that attach after the output was
/// produced. Not thread-safe; <see cref="TerminalStream"/> guards it.
/// </summary>
internal sealed class ReplayBuffer
{
	private readonly int _capacity;
	private byte[]? _buffer;
	private int _start;
	private int _length;
	private bool _trimmed;

	public ReplayBuffer(int capacityBytes) => _capacity = Math.Max(0, capacityBytes);

	public int Length => _length;

	public void Append(ReadOnlySpan<byte> data)
	{
		if (_capacity == 0 || data.IsEmpty)
		{
			return;
		}

		// Allocated on first output so a session that never prints costs nothing.
		_buffer ??= new byte[_capacity];
		if (data.Length >= _capacity)
		{
			data[^_capacity..].CopyTo(_buffer);
			_start = 0;
			_trimmed |= _length > 0 || data.Length > _capacity;
			_length = _capacity;
			return;
		}

		int end = (_start + _length) % _capacity;
		int head = Math.Min(data.Length, _capacity - end);
		data[..head].CopyTo(_buffer.AsSpan(end));
		data[head..].CopyTo(_buffer);

		int overflow = _length + data.Length - _capacity;
		if (overflow > 0)
		{
			_start = (_start + overflow) % _capacity;
			_length = _capacity;
			_trimmed = true;
		}
		else
		{
			_length += data.Length;
		}
	}

	/// <summary>A pooled copy of the buffered output, oldest byte first, or null when nothing is buffered.</summary>
	public OutputChunk? Snapshot()
	{
		if (_buffer is null || _length == 0)
		{
			return null;
		}

		// Trimming can cut a UTF-8 character in half; its orphaned continuation bytes would render as garbage.
		int skip = 0;
		while (_trimmed && skip < 3 && skip < _length && (_buffer[(_start + skip) % _capacity] & 0xC0) == 0x80)
		{
			skip++;
		}

		int count = _length - skip;
		if (count == 0)
		{
			return null;
		}

		OutputChunk chunk = OutputChunk.Rent(count);
		int offset = (_start + skip) % _capacity;
		int head = Math.Min(count, _capacity - offset);
		_buffer.AsSpan(offset, head).CopyTo(chunk.Buffer);
		_buffer.AsSpan(0, count - head).CopyTo(chunk.Buffer.AsSpan(head));
		return chunk;
	}
}
