namespace Mokaterm.Modules.Rdp.Tests;

public sealed class RdpSettingsTests
{
	[Fact]
	public void Defaults_AreUsable()
	{
		RdpSettings settings = new();

		Assert.Equal("rdp", RdpSettings.SectionKey);
		Assert.Equal(20, settings.ConnectTimeoutSeconds);
		Assert.Equal(3, settings.AuthenticationAttempts);
		Assert.Equal(30, settings.FramesPerSecond);
		Assert.Equal(64, settings.RawFrameKilobytes);
		Assert.True(settings.ReleaseKeysWhenInactive);
		Assert.Equal(settings, settings.Clamped());
	}

	[Fact]
	public void Clamped_PullsEveryValueBackIntoRange()
	{
		RdpSettings settings = new()
		{
			ConnectTimeoutSeconds = 0,
			AuthenticationAttempts = 0,
			FramesPerSecond = 1000,
			RawFrameKilobytes = 0,
		};

		RdpSettings clamped = settings.Clamped();

		Assert.Equal(RdpSettings.MinTimeoutSeconds, clamped.ConnectTimeoutSeconds);
		Assert.Equal(1, clamped.AuthenticationAttempts);
		Assert.Equal(RdpSettings.MaxFramesPerSecond, clamped.FramesPerSecond);
		Assert.Equal(RdpSettings.MinRawFrameKilobytes, clamped.RawFrameKilobytes);
	}

	[Fact]
	public void Clamped_CutsValuesThatAreTooLarge()
	{
		RdpSettings clamped = new RdpSettings
		{
			ConnectTimeoutSeconds = 10_000,
			AuthenticationAttempts = 99,
			RawFrameKilobytes = 100_000,
		}.Clamped();

		Assert.Equal(RdpSettings.MaxConnectTimeoutSeconds, clamped.ConnectTimeoutSeconds);
		Assert.Equal(RdpSettings.MaxAuthenticationAttempts, clamped.AuthenticationAttempts);
		Assert.Equal(RdpSettings.MaxRawFrameKilobytes, clamped.RawFrameKilobytes);
	}

	[Theory]
	[InlineData(30, 33)]
	[InlineData(60, 16)]
	[InlineData(10, 100)]
	public void FrameInterval_FollowsTheFrameRate(int framesPerSecond, int milliseconds)
	{
		RdpSettings settings = new() { FramesPerSecond = framesPerSecond };

		Assert.Equal(milliseconds, (int)settings.FrameInterval.TotalMilliseconds);
	}

	[Fact]
	public void FrameInterval_StaysSensibleForAFrameRateOutsideTheRange()
	{
		Assert.Equal(1000.0 / RdpSettings.MinFramesPerSecond, new RdpSettings { FramesPerSecond = 0 }.FrameInterval.TotalMilliseconds, 3);
		Assert.Equal(1000.0 / RdpSettings.MaxFramesPerSecond, new RdpSettings { FramesPerSecond = 500 }.FrameInterval.TotalMilliseconds, 3);
	}
}
