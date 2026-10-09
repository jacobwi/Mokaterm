using Mokaterm.Maui.Platforms.Windows;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The desktop host's screen-to-page arithmetic for native file drops. A real drag cannot be driven from a test, so
/// this is the part that can be: a mistake in it leaves the overlay on the wrong panel on every monitor that is not
/// at 100%, which is the one thing nobody notices while developing on one that is.
/// </summary>
public sealed class NativeDropPointTests
{
	// 96 is 100%, 120 is 125%, 144 is 150% and 192 is 200%: the scales Windows offers without a custom setting.
	[Theory]
	[InlineData(96u, 1.0)]
	[InlineData(120u, 1.25)]
	[InlineData(144u, 1.5)]
	[InlineData(192u, 2.0)]
	public void ScaleFor_TakesTheWindowsScaleFromItsDpi(uint dpi, double scale) =>
		Assert.Equal(scale, NativeDropPoint.ScaleFor(dpi));

	[Fact]
	public void ScaleFor_ReadsAWindowThatReportsNoDpiAsUnscaled() =>
		Assert.Equal(1.0, NativeDropPoint.ScaleFor(0));

	[Theory]
	[InlineData(96u, 300, 200, 300, 200)]
	[InlineData(120u, 300, 200, 240, 160)]
	[InlineData(144u, 300, 200, 200, 133.333)]
	[InlineData(192u, 300, 200, 150, 100)]
	public void ToCss_DividesTheWindowsPixelsByItsScale(uint dpi, int clientX, int clientY, double x, double y)
	{
		(double cssX, double cssY) = NativeDropPoint.ToCss(clientX, clientY, dpi, 0, 0);
		Assert.Equal(x, cssX, 3);
		Assert.Equal(y, cssY, 3);
	}

	[Fact]
	public void ToCss_LandsInTheMiddleOfThePageForTheMiddleOfTheWindow()
	{
		// A 1920x1080 client area at 150% is a 1280x720 page, so its centre is where its centre should be.
		(double x, double y) = NativeDropPoint.ToCss(960, 540, 144, 0, 0);
		Assert.Equal(640, x, 3);
		Assert.Equal(360, y, 3);
	}

	[Fact]
	public void ToCss_MeasuresFromTheWebViewAndNotFromTheWindow()
	{
		// The origin is already in page pixels, so it comes off after the scaling and not before it.
		(double x, double y) = NativeDropPoint.ToCss(300, 200, 144, 12, 48);
		Assert.Equal(188, x, 3);
		Assert.Equal(85.333, y, 3);
	}

	[Fact]
	public void ToCss_KeepsAPointAboveAndLeftOfThePageOutsideIt()
	{
		(double x, double y) = NativeDropPoint.ToCss(0, 0, 192, 10, 20);
		Assert.Equal(-10, x, 3);
		Assert.Equal(-20, y, 3);
	}

	[Theory]
	[InlineData(0, 0)]
	[InlineData(1, 1)]
	[InlineData(1919, 1079)]
	[InlineData(-1920, 480)]
	[InlineData(int.MinValue, int.MaxValue)]
	public void ScreenPoint_TakesApartThePointAnOleCallbackPacked(int x, int y)
	{
		// What the shell hands over: x in the low half, y in the high half, both signed because a monitor left of the
		// primary one has negative screen coordinates.
		long packed = ((long)(uint)y << 32) | (uint)x;
		Assert.Equal((x, y), NativeDropPoint.ScreenPoint(packed));
	}
}
