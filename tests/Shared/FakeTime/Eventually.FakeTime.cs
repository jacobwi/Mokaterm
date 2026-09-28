using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Mokaterm.Tests.Shared;

internal static partial class Eventually
{
	/// <summary>
	/// Advances fake time in steps until <paramref name="condition"/> holds. Stepping copes with a timer that the code
	/// under test registers a moment after the test starts advancing.
	/// </summary>
	public static async Task AdvanceUntilAsync(FakeTimeProvider time, TimeSpan step, Func<bool> condition, string description, int maxSteps = 60)
	{
		ArgumentNullException.ThrowIfNull(time);
		for (int i = 0; i < maxSteps; i++)
		{
			if (await WithinAsync(condition, TimeSpan.FromMilliseconds(40)))
			{
				return;
			}

			time.Advance(step);
		}

		await TrueAsync(condition, description);
	}

	private static async Task<bool> WithinAsync(Func<bool> condition, TimeSpan window)
	{
		Stopwatch elapsed = Stopwatch.StartNew();
		while (elapsed.Elapsed < window)
		{
			if (condition())
			{
				return true;
			}

			await Task.Delay(5, Token);
		}

		return condition();
	}
}
