namespace Mokaterm.UI.Terminal.Playback;

/// <summary>
/// Where a player is in a recording: a position that runs at the chosen speed while playing and stands still while
/// paused, never past the end. The position is worked out from the time each change was made rather than counted up by
/// ticks, so a tick that comes late never makes the playback drift.
/// </summary>
internal sealed class RecordingClock
{
	private readonly TimeProvider _time;
	private TimeSpan _base;
	private long _startedAt;

	public RecordingClock(TimeSpan duration, TimeProvider time)
	{
		Duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
		_time = time;
	}

	public TimeSpan Duration { get; }

	public double Speed { get; private set; } = 1;

	public bool IsPlaying { get; private set; }

	public TimeSpan Position
	{
		get
		{
			if (!IsPlaying)
			{
				return _base;
			}

			TimeSpan played = _base + (_time.GetElapsedTime(_startedAt) * Speed);
			return played < Duration ? played : Duration;
		}
	}

	public bool IsAtEnd => Position >= Duration;

	/// <summary>Starts or carries on. At the end, playing starts over from the beginning.</summary>
	public void Play()
	{
		if (IsPlaying)
		{
			return;
		}

		if (IsAtEnd)
		{
			_base = TimeSpan.Zero;
		}

		_startedAt = _time.GetTimestamp();
		IsPlaying = true;
	}

	public void Pause()
	{
		if (IsPlaying)
		{
			_base = Position;
			IsPlaying = false;
		}
	}

	/// <summary>Moves to <paramref name="position"/>, held inside the recording, playing on from there if it was playing.</summary>
	public void Seek(TimeSpan position)
	{
		_base = position < TimeSpan.Zero ? TimeSpan.Zero : position > Duration ? Duration : position;
		_startedAt = _time.GetTimestamp();
	}

	/// <exception cref="ArgumentOutOfRangeException"><paramref name="speed"/> is not a positive number.</exception>
	public void SetSpeed(double speed)
	{
		if (!double.IsFinite(speed) || speed <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(speed), speed, "The speed must be a positive number.");
		}

		_base = Position;
		_startedAt = _time.GetTimestamp();
		Speed = speed;
	}
}
