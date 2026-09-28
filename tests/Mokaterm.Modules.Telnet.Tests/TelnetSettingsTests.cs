namespace Mokaterm.Modules.Telnet.Tests;

public sealed class TelnetSettingsTests
{
	[Fact]
	public void SectionKey_IsTelnet() => Assert.Equal("telnet", TelnetSettings.SectionKey);

	[Fact]
	public void Defaults_AreUsable()
	{
		TelnetSettings settings = new();

		Assert.Equal(settings, settings.Clamped());
		Assert.Equal(0, settings.KeepAliveSeconds);
		Assert.Equal(TimeSpan.Zero, settings.KeepAlive);
	}

	[Fact]
	public void Clamped_PullsEveryValueIntoRange()
	{
		TelnetSettings settings = new()
		{
			ConnectTimeoutSeconds = 0,
			NegotiationTimeoutSeconds = 0,
			AutoLoginTimeoutSeconds = 1,
			KeepAliveSeconds = -5,
		};

		TelnetSettings clamped = settings.Clamped();

		Assert.Equal(TelnetSettings.MinConnectTimeoutSeconds, clamped.ConnectTimeoutSeconds);
		Assert.Equal(TelnetSettings.MinNegotiationTimeoutSeconds, clamped.NegotiationTimeoutSeconds);
		Assert.Equal(TelnetSettings.MinAutoLoginTimeoutSeconds, clamped.AutoLoginTimeoutSeconds);
		Assert.Equal(0, clamped.KeepAliveSeconds);
	}

	[Fact]
	public void Clamped_HugeValues_StopAtTheMaximum()
	{
		TelnetSettings clamped = new TelnetSettings
		{
			ConnectTimeoutSeconds = int.MaxValue,
			NegotiationTimeoutSeconds = int.MaxValue,
			AutoLoginTimeoutSeconds = int.MaxValue,
			KeepAliveSeconds = int.MaxValue,
		}.Clamped();

		Assert.Equal(TelnetSettings.MaxConnectTimeoutSeconds, clamped.ConnectTimeoutSeconds);
		Assert.Equal(TelnetSettings.MaxNegotiationTimeoutSeconds, clamped.NegotiationTimeoutSeconds);
		Assert.Equal(TelnetSettings.MaxAutoLoginTimeoutSeconds, clamped.AutoLoginTimeoutSeconds);
		Assert.Equal(TelnetSettings.MaxKeepAliveSeconds, clamped.KeepAliveSeconds);
	}

	[Fact]
	public void KeepAlive_AboveZero_IsThatManySeconds()
	{
		TelnetSettings settings = new() { KeepAliveSeconds = 45 };

		Assert.Equal(TimeSpan.FromSeconds(45), settings.KeepAlive);
	}
}
