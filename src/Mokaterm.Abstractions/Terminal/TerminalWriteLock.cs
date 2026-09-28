using System.Diagnostics.CodeAnalysis;

namespace Mokaterm.Abstractions.Terminal;

/// <summary>
/// One writer at a time on a protocol's outbound side, so keystrokes, protocol replies and out of band signals cannot
/// cut into each other's bytes.
/// </summary>
/// <remarks>
/// Never disposed: a write still unwinding after the session closed may wait on it, and a disposed
/// <see cref="SemaphoreSlim"/> throws where such a write should simply stop. It holds no unmanaged handle.
/// </remarks>
[SuppressMessage(
	"Design",
	"CA1001:Types that own disposable fields should be disposable",
	Justification = "The semaphore outlives the session on purpose; see the remarks.")]
public sealed class TerminalWriteLock
{
	private readonly SemaphoreSlim _lock = new(1, 1);

	/// <summary>
	/// Takes the lock, or returns false once there is nothing left to write to. Release it with
	/// <see cref="Release"/> in a finally block.
	/// </summary>
	public async ValueTask<bool> TryEnterAsync(CancellationToken cancellationToken)
	{
		try
		{
			await _lock.WaitAsync(cancellationToken);
			return true;
		}
		catch (ObjectDisposedException)
		{
			return false;
		}
	}

	public void Release() => _lock.Release();
}
