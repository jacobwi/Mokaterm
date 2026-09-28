using Microsoft.Extensions.Time.Testing;
using Mokaterm.Core.Security;

namespace Mokaterm.Core.Tests.Security;

public sealed class UnlockThrottleTests
{
	private readonly FakeTimeProvider _time = new();
	private readonly UnlockThrottle _throttle;

	public UnlockThrottleTests() => _throttle = new UnlockThrottle(_time);

	[Fact]
	public void TryBegin_WithinFreeFailures_IsAllowed()
	{
		Fail(UnlockThrottle.FreeFailures);

		Assert.True(_throttle.TryBegin(out TimeSpan retryAfter));
		Assert.Equal(TimeSpan.Zero, retryAfter);
	}

	[Theory]
	[InlineData(1, 1)]
	[InlineData(2, 2)]
	[InlineData(3, 4)]
	[InlineData(4, 8)]
	public void TryBegin_AfterFreeFailures_DoublesDelay(int extraFailures, int expectedSeconds)
	{
		Fail(UnlockThrottle.FreeFailures + extraFailures);

		Assert.False(_throttle.TryBegin(out TimeSpan retryAfter));
		Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), retryAfter);
	}

	[Fact]
	public void TryBegin_ManyFailures_CapsDelayAtFiveMinutes()
	{
		Fail(UnlockThrottle.FreeFailures + 40);

		Assert.False(_throttle.TryBegin(out TimeSpan retryAfter));
		Assert.Equal(UnlockThrottle.MaxDelay, retryAfter);
		Assert.Equal(TimeSpan.FromMinutes(5), retryAfter);
	}

	[Fact]
	public void TryBegin_AfterDelayElapses_IsAllowedAgain()
	{
		Fail(UnlockThrottle.FreeFailures + 2);
		_time.Advance(TimeSpan.FromMilliseconds(1500));

		Assert.False(_throttle.TryBegin(out TimeSpan retryAfter));
		Assert.Equal(TimeSpan.FromMilliseconds(500), retryAfter);

		_time.Advance(TimeSpan.FromMilliseconds(500));
		Assert.True(_throttle.TryBegin(out _));
	}

	[Fact]
	public void Reset_ClearsFailures()
	{
		Fail(UnlockThrottle.FreeFailures + 3);

		_throttle.Reset();

		Assert.True(_throttle.TryBegin(out _));
		Fail(UnlockThrottle.FreeFailures);
		Assert.True(_throttle.TryBegin(out _));
	}

	private void Fail(int count)
	{
		for (int i = 0; i < count; i++)
		{
			_throttle.RecordFailure();
		}
	}
}
