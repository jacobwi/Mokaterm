namespace Mokaterm.Modules.Ssh.Tests.Fakes;

/// <summary>A stream that says nothing until it is closed, like a command that runs and prints nothing.</summary>
internal sealed class BlockingStream : Stream
{
	private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private int _reading;

	public bool Reading => Volatile.Read(ref _reading) != 0;

	public override bool CanRead => true;

	public override bool CanSeek => false;

	public override bool CanWrite => false;

	public override long Length => throw new NotSupportedException();

	public override long Position
	{
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
	{
		_ = Interlocked.Exchange(ref _reading, 1);
		await _closed.Task;
		throw new ObjectDisposedException(nameof(BlockingStream));
	}

	public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

	public override void Flush()
	{
	}

	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

	public override void SetLength(long value) => throw new NotSupportedException();

	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

	protected override void Dispose(bool disposing)
	{
		_ = _closed.TrySetResult();
		base.Dispose(disposing);
	}
}
