namespace Mokaterm.Modules.Vnc.Tests;

public sealed class VncSettingsTests
{
	[Fact]
	public void SectionKey_IsVnc() => Assert.Equal("vnc", VncSettings.SectionKey);

	[Fact]
	public void Defaults_AreUsable()
	{
		VncSettings settings = new();

		Assert.Equal(settings, settings.Clamped());
		Assert.True(settings.ShowDotCursor);
	}

	[Fact]
	public void Clamped_PullsEveryValueIntoRange()
	{
		VncSettings settings = new()
		{
			ConnectTimeoutSeconds = 0,
			AuthenticationAttempts = 99,
			ClipboardKilobytes = 0,
		};

		VncSettings clamped = settings.Clamped();

		Assert.Equal(VncSettings.MinTimeoutSeconds, clamped.ConnectTimeoutSeconds);
		Assert.Equal(VncSettings.MaxAuthenticationAttempts, clamped.AuthenticationAttempts);
		Assert.Equal(VncSettings.MinClipboardKilobytes, clamped.ClipboardKilobytes);
	}

	[Fact]
	public void Clamped_HugeTimeout_StopsAtTheMaximum()
	{
		VncSettings clamped = new VncSettings { ConnectTimeoutSeconds = int.MaxValue }.Clamped();

		Assert.Equal(VncSettings.MaxConnectTimeoutSeconds, clamped.ConnectTimeoutSeconds);
	}
}
