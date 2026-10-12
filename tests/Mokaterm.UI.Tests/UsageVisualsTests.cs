using Mokaterm.UI.Common.Components;

namespace Mokaterm.UI.Tests;

/// <summary>The sparkline and the usage bar every tool draws its figures with.</summary>
public sealed class UsageVisualsTests
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

	[Theory]
	[InlineData(0, UsageLevel.Normal)]
	[InlineData(74.9, UsageLevel.Normal)]
	[InlineData(75, UsageLevel.Warn)]
	[InlineData(89.9, UsageLevel.Warn)]
	[InlineData(90, UsageLevel.Full)]
	[InlineData(250, UsageLevel.Full)]
	public void UsageBar_Thresholds(double percent, UsageLevel expected) => Assert.Equal(expected, UsageBar.LevelOf(percent));

	[Fact]
	public async Task UsageBar_WithALabel_IsAMeter_ClampedAndColoured()
	{
		string html = await RenderAsync<UsageBar>(new() { ["Percent"] = 130.0, ["Label"] = "Disk used" });

		Assert.Contains("role=\"meter\"", html, StringComparison.Ordinal);
		Assert.Contains("aria-valuenow=\"100\"", html, StringComparison.Ordinal);
		Assert.Contains("mt-usage--full", html, StringComparison.Ordinal);
		Assert.Contains("width:100%", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task UsageBar_WithoutALabel_HidesFromScreenReaders_AndNullIsEmpty()
	{
		string html = await RenderAsync<UsageBar>(new() { ["Percent"] = null });

		Assert.Contains("aria-hidden=\"true\"", html, StringComparison.Ordinal);
		Assert.DoesNotContain("role=", html, StringComparison.Ordinal);
		Assert.Contains("width:0%", html, StringComparison.Ordinal);
		Assert.DoesNotContain("mt-usage--", html, StringComparison.Ordinal);
	}

	[Fact]
	public void Sparkline_FewerThanTwoValues_DrawsNothing()
	{
		Assert.Equal("", Sparkline.Points([], null, null));
		Assert.Equal("", Sparkline.Points([5], 10, 40));
	}

	[Fact]
	public void Sparkline_AFixedMax_PutsValuesAtTheirHeight_AndClampsTheRest()
	{
		// Bottom is y 23, top y 1: a pixel of room each side in the 24 high box.
		Assert.Equal("0,23 50,12 100,1", Sparkline.Points([0, 50, 400], 100, null));
		Assert.Equal("0,23 100,23", Sparkline.Points([-5, double.NaN], 100, null));
	}

	[Fact]
	public void Sparkline_WithoutAMax_ScalesToTheLargestSample()
	{
		Assert.Equal("0,12 100,1", Sparkline.Points([2, 4], null, null));
		Assert.Equal("0,23 100,23", Sparkline.Points([0, 0], null, null));
	}

	[Fact]
	public void Sparkline_Slots_KeepTheNewestAtTheRightEdge()
	{
		// Three samples in five slots start at the middle slot instead of stretching over the width.
		Assert.Equal("50,23 75,23 100,23", Sparkline.Points([0, 0, 0], 10, 5));

		// More samples than slots spread over the width like no slots at all.
		Assert.Equal(Sparkline.Points([1, 2, 3], 10, null), Sparkline.Points([1, 2, 3], 10, 2));
	}

	[Fact]
	public async Task Sparkline_WithALabel_IsAnImage_WithoutOne_IsHidden()
	{
		string named = await RenderAsync<Sparkline>(new() { ["Values"] = new List<double> { 1, 2 }, ["Label"] = "Recent CPU samples", ["Height"] = 18 });
		string hidden = await RenderAsync<Sparkline>(new() { ["Values"] = new List<double> { 1, 2 } });

		Assert.Contains("role=\"img\"", named, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Recent CPU samples\"", named, StringComparison.Ordinal);
		Assert.Contains("height:18px", named, StringComparison.Ordinal);
		Assert.Contains("points=\"0,12 100,1\"", named, StringComparison.Ordinal);
		Assert.Contains("aria-hidden=\"true\"", hidden, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters)
		where TComponent : Microsoft.AspNetCore.Components.IComponent =>
		StaticRender.RenderAsync<TComponent>(_ => { }, parameters).WaitAsync(Wait, TestContext.Current.CancellationToken);
}
