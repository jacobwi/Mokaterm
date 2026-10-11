using Microsoft.Extensions.Time.Testing;
using Mokaterm.UI.Terminal.Playback;

namespace Mokaterm.UI.Tests;

public sealed class RecordingClockTests
{
	private readonly FakeTimeProvider _time = new();

	[Fact]
	public void Position_StandsStillUntilPlayed()
	{
		RecordingClock clock = new(TimeSpan.FromSeconds(10), _time);

		_time.Advance(TimeSpan.FromSeconds(3));

		Assert.False(clock.IsPlaying);
		Assert.Equal(TimeSpan.Zero, clock.Position);
	}

	[Fact]
	public void Play_RunsWithTheClock_AndPauseHoldsWhereItWas()
	{
		RecordingClock clock = new(TimeSpan.FromSeconds(10), _time);

		clock.Play();
		_time.Advance(TimeSpan.FromSeconds(2));
		clock.Pause();
		_time.Advance(TimeSpan.FromSeconds(5));

		Assert.Equal(TimeSpan.FromSeconds(2), clock.Position);
		clock.Play();
		_time.Advance(TimeSpan.FromSeconds(1));
		Assert.Equal(TimeSpan.FromSeconds(3), clock.Position);
	}

	[Fact]
	public void Position_NeverPassesTheEnd()
	{
		RecordingClock clock = new(TimeSpan.FromSeconds(10), _time);

		clock.Play();
		_time.Advance(TimeSpan.FromMinutes(5));

		Assert.Equal(TimeSpan.FromSeconds(10), clock.Position);
		Assert.True(clock.IsAtEnd);
	}

	[Fact]
	public void Play_AtTheEnd_StartsOver()
	{
		RecordingClock clock = new(TimeSpan.FromSeconds(10), _time);
		clock.Play();
		_time.Advance(TimeSpan.FromSeconds(12));
		clock.Pause();

		clock.Play();
		_time.Advance(TimeSpan.FromSeconds(1));

		Assert.Equal(TimeSpan.FromSeconds(1), clock.Position);
	}

	[Fact]
	public void SetSpeed_ScalesWhatComesAfter_NotWhatWasAlreadyPlayed()
	{
		RecordingClock clock = new(TimeSpan.FromSeconds(60), _time);
		clock.Play();
		_time.Advance(TimeSpan.FromSeconds(2));

		clock.SetSpeed(4);
		_time.Advance(TimeSpan.FromSeconds(3));

		Assert.Equal(TimeSpan.FromSeconds(14), clock.Position);
		Assert.Equal(4, clock.Speed);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(double.NaN)]
	[InlineData(double.PositiveInfinity)]
	public void SetSpeed_RefusesWhatIsNotAPositiveNumber(double speed) =>
		Assert.Throws<ArgumentOutOfRangeException>(() => new RecordingClock(TimeSpan.FromSeconds(1), _time).SetSpeed(speed));

	[Fact]
	public void Seek_MovesThePosition_HeldInsideTheRecording_AndKeepsPlaying()
	{
		RecordingClock clock = new(TimeSpan.FromSeconds(10), _time);
		clock.Play();
		_time.Advance(TimeSpan.FromSeconds(1));

		clock.Seek(TimeSpan.FromSeconds(7));
		_time.Advance(TimeSpan.FromSeconds(1));
		Assert.Equal(TimeSpan.FromSeconds(8), clock.Position);

		clock.Seek(TimeSpan.FromSeconds(-3));
		Assert.Equal(TimeSpan.Zero, clock.Position);
		clock.Seek(TimeSpan.FromMinutes(1));
		Assert.Equal(TimeSpan.FromSeconds(10), clock.Position);
	}

	[Fact]
	public void Seek_WhilePaused_StaysPaused()
	{
		RecordingClock clock = new(TimeSpan.FromSeconds(10), _time);

		clock.Seek(TimeSpan.FromSeconds(4));
		_time.Advance(TimeSpan.FromSeconds(2));

		Assert.False(clock.IsPlaying);
		Assert.Equal(TimeSpan.FromSeconds(4), clock.Position);
	}
}
