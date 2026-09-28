using System.Collections.Concurrent;

namespace Mokaterm.Core.Storage;

/// <summary>One async mutex per key. Keys are document names, a small fixed set, so entries are never removed.</summary>
internal sealed class AsyncKeyedLock
{
	private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new(StringComparer.Ordinal);

	public async ValueTask<Releaser> AcquireAsync(string key, CancellationToken cancellationToken)
	{
		SemaphoreSlim semaphore = _semaphores.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
		await semaphore.WaitAsync(cancellationToken);
		return new Releaser(semaphore);
	}

	public readonly struct Releaser : IDisposable
	{
		private readonly SemaphoreSlim? _semaphore;

		internal Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

		public void Dispose() => _semaphore?.Release();
	}
}
