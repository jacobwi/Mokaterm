using Mokaterm.Abstractions.FileSystem;

namespace Mokaterm.Modules.Ssh.FileSystem;

/// <summary>
/// A remote file opened for reading. Every call goes through the owning file system's lock, because an SSH.NET SFTP
/// client is not documented as safe for concurrent use.
/// </summary>
internal sealed class SftpReadStream : Stream
{
	private readonly Stream _inner;
	private readonly SemaphoreSlim _gate;
	private readonly string _path;
	private int _disposed;

	public SftpReadStream(Stream inner, SemaphoreSlim gate, string path)
	{
		_inner = inner;
		_gate = gate;
		_path = path;
	}

	public override bool CanRead => Volatile.Read(ref _disposed) == 0;

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
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		await _gate.WaitAsync(cancellationToken);
		try
		{
			return await _inner.ReadAsync(buffer, cancellationToken);
		}
		catch (Exception ex) when (RemoteFileErrors.TryMap(ex, _path, out RemoteFileSystemException? mapped))
		{
			throw mapped;
		}
		finally
		{
			_gate.Release();
		}
	}

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
		ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

	public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

	public override int Read(Span<byte> buffer)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		_gate.Wait();
		try
		{
			return _inner.Read(buffer);
		}
		catch (Exception ex) when (RemoteFileErrors.TryMap(ex, _path, out RemoteFileSystemException? mapped))
		{
			throw mapped;
		}
		finally
		{
			_gate.Release();
		}
	}

	public override void Flush()
	{
	}

	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

	public override void SetLength(long value) => throw new NotSupportedException();

	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

	public override async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 0)
		{
			await _gate.WaitAsync();
			try
			{
				await _inner.DisposeAsync();
			}
			catch (Exception ex) when (RemoteFileErrors.TryMap(ex, _path, out _))
			{
				// Closing the remote handle failed because the connection is gone; nothing is left to release.
			}
			finally
			{
				_gate.Release();
			}
		}

		await base.DisposeAsync();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
		{
			_gate.Wait();
			try
			{
				_inner.Dispose();
			}
			catch (Exception ex) when (RemoteFileErrors.TryMap(ex, _path, out _))
			{
				// Closing the remote handle failed because the connection is gone; nothing is left to release.
			}
			finally
			{
				_gate.Release();
			}
		}

		base.Dispose(disposing);
	}
}
