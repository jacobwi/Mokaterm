using System.Text;
using Mokaterm.Abstractions.Terminal;

namespace Mokaterm.Sessions.Tests.Fakes;

/// <summary>Copies everything it receives. <see cref="OnWrite"/> runs first, to slow the sink down or make it throw.</summary>
internal sealed class RecordingSink : ITerminalSink
{
	private readonly Lock _lock = new();
	private readonly MemoryStream _received = new();
	private int _writeCount;

	public Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? OnWrite { get; init; }

	public int WriteCount => Volatile.Read(ref _writeCount);

	public long Length
	{
		get
		{
			lock (_lock)
			{
				return _received.Length;
			}
		}
	}

	public string Text
	{
		get
		{
			lock (_lock)
			{
				return Encoding.UTF8.GetString(_received.GetBuffer(), 0, (int)_received.Length);
			}
		}
	}

	public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
	{
		_ = Interlocked.Increment(ref _writeCount);
		if (OnWrite is not null)
		{
			await OnWrite(data, cancellationToken);
		}

		lock (_lock)
		{
			_received.Write(data.Span);
		}
	}
}
