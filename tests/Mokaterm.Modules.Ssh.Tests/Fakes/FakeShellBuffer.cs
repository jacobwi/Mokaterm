namespace Mokaterm.Modules.Ssh.Tests.Fakes;

/// <summary>
/// Buffers shell output the way SSH.NET's ShellStream does: whatever arrives is kept, the buffer grows without limit, and
/// a read blocks until there is data or the shell closed.
/// </summary>
internal sealed class FakeShellBuffer
{
	private readonly object _sync = new();
	private readonly Queue<byte> _bytes = new();
	private bool _closed;

	public long Length
	{
		get
		{
			lock (_sync)
			{
				return _bytes.Count;
			}
		}
	}

	public void Append(ReadOnlySpan<byte> data)
	{
		lock (_sync)
		{
			foreach (byte b in data)
			{
				_bytes.Enqueue(b);
			}

			Monitor.PulseAll(_sync);
		}
	}

	public int Read(byte[] buffer)
	{
		lock (_sync)
		{
			while (_bytes.Count == 0 && !_closed)
			{
				_ = Monitor.Wait(_sync);
			}

			int count = Math.Min(buffer.Length, _bytes.Count);
			for (int i = 0; i < count; i++)
			{
				buffer[i] = _bytes.Dequeue();
			}

			return count;
		}
	}

	public void Close()
	{
		lock (_sync)
		{
			_closed = true;
			Monitor.PulseAll(_sync);
		}
	}
}
