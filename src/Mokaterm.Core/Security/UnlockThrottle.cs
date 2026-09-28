namespace Mokaterm.Core.Security;

/// <summary>
/// Slows down password guessing across every UI scope. The first <see cref="FreeFailures"/> failures cost nothing;
/// each later failure locks out attempts for 1s, 2s, 4s and so on, up to <see cref="MaxDelay"/>. A success resets it.
/// </summary>
internal sealed class UnlockThrottle : IDisposable
{
	public const int FreeFailures = 5;

	public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(5);

	private readonly TimeProvider _timeProvider;
	private readonly Lock _sync = new();

	// One password check at a time across every scope. TryBegin and the RecordFailure after it only count as one step
	// inside a turn: without it every browser tab or circuit could pass TryBegin before the first failure is recorded,
	// multiplying the guesses per lockout, and each would run its own 64 MiB Argon2 derivation at the same time.
	private readonly SemaphoreSlim _turn = new(1, 1);

	private int _failures;
	private DateTimeOffset _blockedUntil;

	public UnlockThrottle(TimeProvider timeProvider) => _timeProvider = timeProvider;

	/// <summary>
	/// Waits until no other scope is checking a password. Call <see cref="TryBegin"/> and record the outcome before
	/// disposing the result.
	/// </summary>
	public async ValueTask<Turn> WaitTurnAsync(CancellationToken cancellationToken)
	{
		await _turn.WaitAsync(cancellationToken);
		return new Turn(_turn);
	}

	/// <summary>Returns false with the remaining wait when attempts are currently blocked.</summary>
	public bool TryBegin(out TimeSpan retryAfter)
	{
		lock (_sync)
		{
			TimeSpan remaining = _blockedUntil - _timeProvider.GetUtcNow();
			retryAfter = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
			return retryAfter == TimeSpan.Zero;
		}
	}

	public void RecordFailure()
	{
		lock (_sync)
		{
			_failures++;
			if (_failures > FreeFailures)
			{
				_blockedUntil = _timeProvider.GetUtcNow() + GetDelay(_failures - FreeFailures);
			}
		}
	}

	public void Reset()
	{
		lock (_sync)
		{
			_failures = 0;
			_blockedUntil = default;
		}
	}

	public void Dispose() => _turn.Dispose();

	/// <summary>The lockout after the <paramref name="penalizedFailure"/>th failure beyond the free ones.</summary>
	private static TimeSpan GetDelay(int penalizedFailure)
	{
		// 2^(n-1) seconds; past 2^9 the cap applies anyway, which also keeps the shift from overflowing.
		int exponent = Math.Min(penalizedFailure - 1, 10);
		TimeSpan delay = TimeSpan.FromSeconds(1 << exponent);
		return delay < MaxDelay ? delay : MaxDelay;
	}

	/// <summary>The right to check one password. Disposing it lets the next scope in.</summary>
	public readonly struct Turn : IDisposable
	{
		private readonly SemaphoreSlim? _semaphore;

		internal Turn(SemaphoreSlim semaphore) => _semaphore = semaphore;

		public void Dispose() => _semaphore?.Release();
	}
}
