using Mokaterm.Maui.Platforms.Windows;
using Mokaterm.UI.Common.Platform;

namespace Mokaterm.UI.Tests;

/// <summary>
/// The desktop host's title bar arithmetic. Windows draws the caption buttons and hit tests them itself; what the app
/// adds is where the page may be dragged, and a rectangle one pixel too wide takes a click meant for a control or for
/// the window's own minimize, maximize and close.
/// </summary>
public sealed class CaptionRegionsTests
{
	// Tall caption buttons are 48 by 48 at 100%, three of them; 144 is 150%, the scale the numbers were measured at.
	[Theory]
	[InlineData(1.0, 144, 48)]
	[InlineData(1.25, 180, 60)]
	[InlineData(1.5, 216, 72)]
	[InlineData(2.0, 288, 96)]
	public void Insets_AreTheCaptionButtonsInCssPixels(double scale, int rightInset, int height) =>
		Assert.Equal(new WindowChromeInsets(0, 144, 48), CaptionRegions.Insets(0, rightInset, height, scale));

	[Theory]
	[InlineData(0.0)]
	[InlineData(-1.0)]
	[InlineData(double.NaN)]
	public void Insets_ReadAScaleTheWindowCannotGiveYetAsUnscaled(double scale) =>
		Assert.Equal(new WindowChromeInsets(0, 216, 72), CaptionRegions.Insets(0, 216, 72, scale));

	[Fact]
	public void FromPage_ScalesARegionIntoWindowPixels()
	{
		CaptionRect[] rects = CaptionRegions.FromPage([new WindowDragRegion(8, 0, 232, 48)], 1.5, 0, 0, 1800, 0, 216);

		Assert.Equal([new CaptionRect(12, 0, 348, 72)], rects);
	}

	[Fact]
	public void FromPage_RoundsInwards()
	{
		// 8.3 to 108.3 by 0.5 to 40.5 at 125% is 10.375 to 135.375 by 0.625 to 50.625.
		CaptionRect[] rects = CaptionRegions.FromPage([new WindowDragRegion(8.3, 0.5, 100, 40)], 1.25, 0, 0, 1500, 0, 180);

		Assert.Equal([new CaptionRect(11, 1, 124, 49)], rects);
	}

	[Fact]
	public void FromPage_StopsAtTheCaptionButtons()
	{
		// A 1200 by 800 page at 150%: the buttons cover the last 144 CSS pixels, so the stretch the page reports up to its
		// right edge ends where minimize starts, and one that lies wholly under them is no stretch at all.
		CaptionRect[] rects = CaptionRegions.FromPage(
			[new WindowDragRegion(816, 0, 384, 48), new WindowDragRegion(1060, 0, 140, 48)],
			1.5,
			0,
			0,
			1800,
			0,
			216);

		Assert.Equal([new CaptionRect(1224, 0, 360, 72)], rects);
	}

	[Fact]
	public void FromPage_StartsAfterTheLeftInset()
	{
		CaptionRect[] rects = CaptionRegions.FromPage([new WindowDragRegion(0, 0, 100, 48)], 1.0, 0, 0, 1200, 30, 144);

		Assert.Equal([new CaptionRect(30, 0, 70, 48)], rects);
	}

	[Fact]
	public void FromPage_MeasuresFromWhereTheWebViewStarts()
	{
		// The origin is in view pixels, so it is added before the scaling and not after it.
		CaptionRect[] rects = CaptionRegions.FromPage([new WindowDragRegion(8, 0, 100, 48)], 1.5, 0, 10, 1800, 0, 216);

		Assert.Equal([new CaptionRect(12, 15, 150, 72)], rects);
	}

	[Fact]
	public void FromPage_DropsWhatTheScriptSentThatIsNoRectangle()
	{
		CaptionRect[] rects = CaptionRegions.FromPage(
			[
				new WindowDragRegion(double.NaN, 0, 100, 48),
				new WindowDragRegion(0, double.PositiveInfinity, 100, 48),
				new WindowDragRegion(0, 0, 0, 48),
				new WindowDragRegion(0, 0, 100, -48),
			],
			1.5,
			0,
			0,
			1800,
			0,
			216);

		Assert.Empty(rects);
	}

	[Fact]
	public void FromPage_DropsAStretchThatRoundsToNothing()
	{
		// 10.1 to 10.6 at 150% is 15.15 to 15.9: no whole pixel inside it.
		Assert.Empty(CaptionRegions.FromPage([new WindowDragRegion(10.1, 0, 0.5, 48)], 1.5, 0, 0, 1800, 0, 216));
	}

	[Fact]
	public void Default_IsTheStripBetweenTheWindowsButtons() =>
		Assert.Equal([new CaptionRect(0, 0, 1584, 72)], CaptionRegions.Default(1800, 0, 216, 72));

	[Theory]
	[InlineData(216, 72)]
	[InlineData(1800, 0)]
	public void Default_IsNothingWithoutRoomForIt(int clientWidth, int height) =>
		Assert.Empty(CaptionRegions.Default(clientWidth, 0, 216, height));
}
