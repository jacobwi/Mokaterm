using System.Diagnostics;

namespace Mokaterm.Tests.Shared;

/// <summary>
/// Waits for conditions reached on background tasks, sockets and loops. Every wait is bounded and fails with what it
/// was waiting for: a hung test would hold the shared build lock for every agent working in this tree. The polling runs
/// on the test's own cancellation token, so nothing here takes one (xUnit1051 is the rule that asks for that).
/// </summary>
internal static partial class Eventually
{
	/// <summary>Generous, because builds and test runs go on in parallel on the same machine.</summary>
	public static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

	/// <summary>How long a test waits to be sure nothing is going to arrive.</summary>
	public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(150);

	private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(5);

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>Polls until <paramref name="condition"/> holds.</summary>
	public static async Task TrueAsync(Func<bool> condition, string description, TimeSpan? limit = null, TimeSpan? poll = null)
	{
		TimeSpan deadline = limit ?? Limit;
		TimeSpan step = poll ?? Poll;
		Stopwatch elapsed = Stopwatch.StartNew();
		while (!condition())
		{
			if (elapsed.Elapsed > deadline)
			{
				Assert.Fail($"Timed out waiting until {description}.");
			}

			await Task.Delay(step, Token);
		}
	}

	/// <summary>Holds when <paramref name="condition"/> stays true for a short real-time window.</summary>
	public static async Task StaysTrueAsync(Func<bool> condition, string description, TimeSpan? window = null)
	{
		TimeSpan hold = window ?? Quiet;
		Stopwatch elapsed = Stopwatch.StartNew();
		while (elapsed.Elapsed < hold)
		{
			Assert.True(condition(), description);
			await Task.Delay(10, Token);
		}
	}

	/// <summary>Waits until <paramref name="read"/> returns the same value for several polls in a row, and returns it.</summary>
	public static async Task<long> StableAsync(Func<long> read, int stablePolls = 6, int pollMilliseconds = 40)
	{
		Stopwatch elapsed = Stopwatch.StartNew();
		long last = read();
		int unchanged = 0;
		while (unchanged < stablePolls)
		{
			if (elapsed.Elapsed > Limit)
			{
				Assert.Fail("Timed out waiting for a value to settle.");
			}

			await Task.Delay(pollMilliseconds, Token);
			long current = read();
			unchanged = current == last ? unchanged + 1 : 0;
			last = current;
		}

		return last;
	}

	/// <summary>Waits for a task that should finish, and fails instead of hanging when it does not.</summary>
	public static Task CompletesAsync(Task task, TimeSpan? limit = null) => task.WaitAsync(limit ?? Limit, Token);
}
