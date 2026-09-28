using Mokaterm.Abstractions.FileSystem;
using Mokaterm.Modules.Ssh.FileSystem;
using Mokaterm.Modules.Ssh.Shell;

namespace Mokaterm.Modules.Ssh.Elevation;

/// <summary>
/// A file read as root with <c>cat</c>. Skips anything login scripts printed before the output marker, and at the end
/// checks that sudo ran the script and it succeeded, so a failure never looks like a short file.
/// </summary>
internal sealed class ElevatedReadStream : Stream
{
	private const int MaxPreambleBytes = 64 * 1024;

	private readonly RemoteCommand _command;
	private readonly Action _release;
	private readonly string _account;
	private readonly string _path;
	private byte[]? _scan;
	private int _scanFilled;
	private int _pendingOffset;
	private int _pendingCount;
	private bool _started;
	private bool _finished;
	private int _disposed;

	/// <param name="release">Frees the command slot; called once on dispose.</param>
	public ElevatedReadStream(RemoteCommand command, Action release, string account, string path)
	{
		_command = command;
		_release = release;
		_account = account;
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
		if (buffer.IsEmpty)
		{
			return 0;
		}

		while (!_started)
		{
			byte[] scan = StartScan();
			int scanned = await ReadOutputAsync(scan.AsMemory(_scanFilled), cancellationToken);
			AcceptScanned(scanned);
		}

		if (TakePending(buffer.Span) is int pending and > 0)
		{
			return pending;
		}

		if (_finished)
		{
			return 0;
		}

		int read = await ReadOutputAsync(buffer, cancellationToken);
		if (read == 0)
		{
			await WaitForExitAsync();
			Finish();
		}

		return read;
	}

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
		ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

	public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

	public override int Read(Span<byte> buffer)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		if (buffer.IsEmpty)
		{
			return 0;
		}

		while (!_started)
		{
			byte[] scan = StartScan();
			AcceptScanned(ReadOutput(scan.AsSpan(_scanFilled)));
		}

		if (TakePending(buffer) is int pending and > 0)
		{
			return pending;
		}

		if (_finished)
		{
			return 0;
		}

		int read = ReadOutput(buffer);
		if (read == 0)
		{
			Finish();
		}

		return read;
	}

	public override void Flush()
	{
	}

	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

	public override void SetLength(long value) => throw new NotSupportedException();

	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

	protected override void Dispose(bool disposing)
	{
		if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
		{
			_command.Dispose();
			_release();
		}

		base.Dispose(disposing);
	}

	private byte[] StartScan()
	{
		_scan ??= new byte[MaxPreambleBytes];
		if (_scanFilled == _scan.Length)
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.Unknown, "The server printed unexpected output instead of the file.", _path);
		}

		return _scan;
	}

	private void AcceptScanned(int read)
	{
		if (read == 0)
		{
			// Throws the real reason (sudo refused, the file is missing) when there is one.
			Finish();
			throw new RemoteFileSystemException(RemoteFileErrorKind.Unknown, "The server ended the transfer before it started.", _path);
		}

		_scanFilled += read;
		int index = _scan.AsSpan(0, _scanFilled).IndexOf(RemoteOutput.Marker);
		if (index >= 0)
		{
			_pendingOffset = index + RemoteOutput.Marker.Length;
			_pendingCount = _scanFilled - _pendingOffset;
			_started = true;
			if (_pendingCount == 0)
			{
				_scan = null;
			}
		}
	}

	private int TakePending(Span<byte> buffer)
	{
		if (_pendingCount == 0)
		{
			return 0;
		}

		int count = Math.Min(buffer.Length, _pendingCount);
		_scan.AsSpan(_pendingOffset, count).CopyTo(buffer);
		_pendingOffset += count;
		_pendingCount -= count;
		if (_pendingCount == 0)
		{
			_scan = null;
		}

		return count;
	}

	private async Task<int> ReadOutputAsync(Memory<byte> buffer, CancellationToken cancellationToken)
	{
		try
		{
			return await _command.Output.ReadAsync(buffer, cancellationToken);
		}
		catch (Exception ex) when (RemoteFileErrors.TryMap(ex, _path, out RemoteFileSystemException? mapped))
		{
			throw mapped;
		}
	}

	private int ReadOutput(Span<byte> buffer)
	{
		try
		{
			return _command.Output.Read(buffer);
		}
		catch (Exception ex) when (RemoteFileErrors.TryMap(ex, _path, out RemoteFileSystemException? mapped))
		{
			throw mapped;
		}
	}

	private async Task WaitForExitAsync()
	{
		try
		{
			await _command.Execution;
		}
		catch (Exception ex) when (RemoteFileErrors.TryMap(ex, _path, out RemoteFileSystemException? mapped))
		{
			throw mapped;
		}
	}

	/// <summary>
	/// Checks how the command ended. SSH.NET completes the command before it closes the output, so once a read returned
	/// 0 the outcome is already known and nothing here waits.
	/// </summary>
	private void Finish()
	{
		Task execution = _command.Execution;
		if (!execution.IsCompleted)
		{
			throw new RemoteFileSystemException(RemoteFileErrorKind.ConnectionLost, "The transfer ended before the server finished sending the file.", _path);
		}

		if (!execution.IsCompletedSuccessfully)
		{
			Exception failure = execution.Exception?.GetBaseException() ?? new OperationCanceledException();
			throw RemoteFileErrors.TryMap(failure, _path, out RemoteFileSystemException? mapped) ? mapped : failure;
		}

		SudoRunner.EnsureElevated(_command.ExitStatus, _command.ReadError(), _account, _path, out string scriptError);
		if (_command.ExitStatus != 0)
		{
			throw RemoteCommandErrors.ToException(_command.ExitStatus, scriptError, _path);
		}

		_finished = true;
	}
}
