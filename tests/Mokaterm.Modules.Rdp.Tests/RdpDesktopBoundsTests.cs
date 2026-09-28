using Mokaterm.Modules.Rdp.Protocol;

namespace Mokaterm.Modules.Rdp.Tests;

/// <summary>The desktop sizes a server may ask for before the decoder allocates the picture.</summary>
public sealed class RdpDesktopBoundsTests
{
	[Theory]
	[InlineData(1920, 1080)]
	[InlineData(1, 1)]
	[InlineData(RdpConnectionOptions.MaxDesktopSize, RdpConnectionOptions.MaxDesktopSize)]
	public void IsSupported_SizesUpToTheLargestDesktop_AreKept(int width, int height) =>
		Assert.True(RdpDesktopBounds.IsSupported(width, height));

	[Theory]
	[InlineData(ushort.MaxValue, ushort.MaxValue)]
	[InlineData(RdpConnectionOptions.MaxDesktopSize + 1, 1080)]
	[InlineData(1920, RdpConnectionOptions.MaxDesktopSize + 1)]
	[InlineData(0, 1080)]
	[InlineData(1920, 0)]
	public void IsSupported_AnEmptyOrOversizedDesktop_IsRefused(int width, int height) =>
		Assert.False(RdpDesktopBounds.IsSupported(width, height));

	[Fact]
	public void DescribeRefusal_NamesTheSizeAndTheLimit()
	{
		string message = RdpDesktopBounds.DescribeRefusal(65535, 65535);

		Assert.Contains("65535 x 65535", message, StringComparison.Ordinal);
		Assert.Contains("8192 x 8192", message, StringComparison.Ordinal);
	}
}
