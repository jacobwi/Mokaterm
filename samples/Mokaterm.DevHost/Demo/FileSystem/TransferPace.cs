using System.Diagnostics;

namespace Mokaterm.DevHost.Demo.FileSystem;

/// <summary>Holds a transfer to about 20 MB/s, so the transfer queue shows progress instead of finishing at memory speed.</summary>
internal sealed class TransferPace
{
	private const double BytesPerSecond = 20 * 1024 * 1024;

	// Shorter waits only add timer jitter; the next longer wait catches up.
	private static readonly TimeSpan MinimumWait = TimeSpan.FromMilliseconds(15);

	private readonly long _started = Stopwatch.GetTimestamp();

	public Task WaitAsync(long transferredBytes, CancellationToken cancellationToken)
	{
		TimeSpan ahead = TimeSpan.FromSeconds(transferredBytes / BytesPerSecond) - Stopwatch.GetElapsedTime(_started);
		return ahead >= MinimumWait ? Task.Delay(ahead, cancellationToken) : Task.CompletedTask;
	}
}
