namespace Mokaterm.DevHost.Demo;

/// <summary>
/// A read-only stream of <see cref="Length"/> bytes that never sit in memory together: a seed repeated to the length, or
/// a noise pattern when there is no seed. The same length and seed always produce the same bytes.
/// </summary>
internal sealed class GeneratedStream : Stream
{
	private readonly long _length;
	private readonly byte[] _seed;
	private long _position;

	public GeneratedStream(long length, byte[]? seed = null)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(length);
		_length = length;
		_seed = seed ?? [];
	}

	public override bool CanRead => true;

	public override bool CanSeek => true;

	public override bool CanWrite => false;

	public override long Length => _length;

	public override long Position
	{
		get => _position;
		set
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			_position = value;
		}
	}

	public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

	public override int Read(Span<byte> buffer)
	{
		int count = (int)Math.Clamp(_length - _position, 0, buffer.Length);
		Span<byte> target = buffer[..count];
		if (_seed.Length > 0)
		{
			for (int written = 0; written < count;)
			{
				int offset = (int)((_position + written) % _seed.Length);
				int chunk = Math.Min(_seed.Length - offset, count - written);
				_seed.AsSpan(offset, chunk).CopyTo(target[written..]);
				written += chunk;
			}
		}
		else
		{
			for (int i = 0; i < count; i++)
			{
				ulong position = (ulong)(_position + i);
				target[i] = (byte)(DemoRandom.Mix(position >> 3) >> (int)((position & 7) * 8));
			}
		}

		_position += count;
		return count;
	}

	public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
		cancellationToken.IsCancellationRequested
			? ValueTask.FromCanceled<int>(cancellationToken)
			: ValueTask.FromResult(Read(buffer.Span));

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
		ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

	public override long Seek(long offset, SeekOrigin origin)
	{
		Position = origin switch
		{
			SeekOrigin.Begin => offset,
			SeekOrigin.Current => _position + offset,
			SeekOrigin.End => _length + offset,
			_ => throw new ArgumentOutOfRangeException(nameof(origin)),
		};

		return _position;
	}

	public override void Flush()
	{
	}

	public override void SetLength(long value) => throw new NotSupportedException();

	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
