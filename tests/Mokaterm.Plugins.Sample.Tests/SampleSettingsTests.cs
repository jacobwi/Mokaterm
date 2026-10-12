namespace Mokaterm.Plugins.Sample.Tests;

public sealed class SampleSettingsTests
{
	[Fact]
	public void Defaults_AreTheOnesThePagesUsedBefore()
	{
		SampleSettings settings = new();

		Assert.Equal(30, settings.Fail2banRefreshSeconds);
		Assert.Equal(SampleSettings.DefaultSubnetStart, settings.SubnetStart);
	}

	[Theory]
	[InlineData(0, SampleSettings.MinFail2banRefreshSeconds)]
	[InlineData(60, 60)]
	[InlineData(1_000_000, SampleSettings.MaxFail2banRefreshSeconds)]
	public void Clamped_KeepsTheRefreshInRange(int seconds, int expected)
	{
		Assert.Equal(expected, new SampleSettings { Fail2banRefreshSeconds = seconds }.Clamped().Fail2banRefreshSeconds);
	}

	[Theory]
	[InlineData("", SampleSettings.DefaultSubnetStart)]
	[InlineData("   ", SampleSettings.DefaultSubnetStart)]
	[InlineData(" 10.0.0.0/8 ", "10.0.0.0/8")]
	public void Clamped_FallsBackToTheDefaultSubnet_ForNothing(string start, string expected)
	{
		Assert.Equal(expected, new SampleSettings { SubnetStart = start }.Clamped().SubnetStart);
	}

	[Fact]
	public void Clamped_RefusesAnOverlongSubnet()
	{
		Assert.Equal(SampleSettings.DefaultSubnetStart, new SampleSettings { SubnetStart = new string('1', 65) }.Clamped().SubnetStart);
	}
}
