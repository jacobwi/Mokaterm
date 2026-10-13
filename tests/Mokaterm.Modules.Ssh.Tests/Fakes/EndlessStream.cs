namespace Mokaterm.Modules.Ssh.Tests.Fakes;

/// <summary>A command that keeps printing, like <c>yes</c>, or prints that much of it and ends.</summary>
/// <param name="length">How much there is in all; null for no end.</param>
internal sealed class EndlessStream(long? length = null) : Stream
{
	private long _served;

	public long Served => Interlocked.Read(ref _served);

	public override bool CanRead => true;

	public override bool CanSeek => false;

	public override bool CanWrite => false;

	public override long Length => throw new NotSupportedException();

	public override long Position
	{
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		if (length is { } total)
		{
			count = (int)Math.Min(count, total - Served);
		}

		buffer.AsSpan(offset, count).Fill((byte)'y');
		_ = Interlocked.Add(ref _served, count);
		return count;
	}

	public override void Flush()
	{
	}

	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

	public override void SetLength(long value) => throw new NotSupportedException();

	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
