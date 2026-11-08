using Mokaterm.UI.Common.Components;

namespace Mokaterm.UI.Tests;

/// <summary>The sparkline and the usage bar every tool draws its figures with, both Moka.Red charts inside.</summary>
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
	public async Task UsageBar_WithALabel_IsAMokaMeter_ClampedAndColoured()
	{
		string html = await RenderAsync<UsageBar>(new() { ["Percent"] = 130.0, ["Label"] = "Disk used" });

		Assert.Contains("moka-meter", html, StringComparison.Ordinal);
		Assert.Contains("role=\"meter\"", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Disk used\"", html, StringComparison.Ordinal);
		Assert.Contains("aria-valuenow=\"100\"", html, StringComparison.Ordinal);
		Assert.Contains("aria-valuetext=\"100%\"", html, StringComparison.Ordinal);
		Assert.Contains("mt-usage--full", html, StringComparison.Ordinal);
		Assert.Contains("moka-meter--error", html, StringComparison.Ordinal);
		Assert.Contains("width: 100%", html, StringComparison.Ordinal);
		Assert.DoesNotContain("aria-hidden", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task UsageBar_WithoutALabel_HidesFromScreenReaders_AndNullIsEmpty()
	{
		string html = await RenderAsync<UsageBar>(new() { ["Percent"] = null });

		Assert.Contains("aria-hidden=\"true\"", html, StringComparison.Ordinal);
		Assert.DoesNotContain("aria-label", html, StringComparison.Ordinal);
		Assert.Contains("width: 0%", html, StringComparison.Ordinal);
		Assert.DoesNotContain("mt-usage--", html, StringComparison.Ordinal);
		Assert.Contains("moka-meter--success", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task UsageBar_KeepsTheCallersClass_AndWarnsFromSeventyFive()
	{
		string html = await RenderAsync<UsageBar>(new() { ["Percent"] = 80.0, ["Class"] = "mt-fleet-bar" });

		Assert.Contains("mt-usage mt-usage--warn mt-fleet-bar", html, StringComparison.Ordinal);
		Assert.Contains("moka-meter--warning", html, StringComparison.Ordinal);
	}

	[Fact]
	public void Sparkline_Window_ReadsWhatIsNotANumberAsZero()
	{
		Assert.Equal([0, 50, 400], Sparkline.Window([-5, 50, 400], null));
		Assert.Equal([0, 0], Sparkline.Window([double.NaN, double.PositiveInfinity], null));
		Assert.Empty(Sparkline.Window([], null));
	}

	[Fact]
	public void Sparkline_Slots_KeepTheNewestAtTheRightEdge()
	{
		double[] window = Sparkline.Window([1, 2, 3], 5);

		// Three samples in five slots start in the middle as gaps, rather than stretching over the width.
		Assert.Equal(5, window.Length);
		Assert.True(double.IsNaN(window[0]) && double.IsNaN(window[1]));
		Assert.Equal([1d, 2d, 3d], window[2..]);

		// More samples than slots spread over the width like no slots at all.
		Assert.Equal(Sparkline.Window([1, 2, 3], null), Sparkline.Window([1, 2, 3], 2));
	}

	[Fact]
	public void Sparkline_Top_IsTheFixedMax_ElseTheLargestSample_ElseOne()
	{
		Assert.Equal(100, Sparkline.Top([0, 50, 400], 100));
		Assert.Equal(4, Sparkline.Top([2, 4], null));
		Assert.Equal(4, Sparkline.Top([2, 4], 0));
		Assert.Equal(1, Sparkline.Top([0, 0], null));
		Assert.Equal(1, Sparkline.Top([], null));
	}

	[Fact]
	public async Task Sparkline_WithALabel_IsAnImage_WithoutOne_IsHidden()
	{
		string named = await RenderAsync<Sparkline>(new() { ["Values"] = new List<double> { 1, 2 }, ["Label"] = "Recent CPU samples", ["Height"] = 18 });
		string hidden = await RenderAsync<Sparkline>(new() { ["Values"] = new List<double> { 1, 2 } });

		Assert.Contains("moka-sparkline", named, StringComparison.Ordinal);
		Assert.Contains("role=\"img\"", named, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Recent CPU samples\"", named, StringComparison.Ordinal);
		Assert.Contains("height:18px", named, StringComparison.Ordinal);
		Assert.Contains("moka-sparkline-line", named, StringComparison.Ordinal);
		Assert.DoesNotContain("aria-hidden", named, StringComparison.Ordinal);
		Assert.Contains("aria-hidden=\"true\"", hidden, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Sparkline_TakesItsColourFromTheCallersVariable()
	{
		string html = await RenderAsync<Sparkline>(new() { ["Values"] = new List<double> { 1, 2 }, ["Class"] = "mta-spark-out" });

		Assert.Contains("mt-sparkline mta-spark-out", html, StringComparison.Ordinal);
		Assert.Contains("--moka-chart-color: var(--mt-sparkline-color, var(--moka-color-primary))", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Sparkline_FewerThanTwoValues_DrawsNoLine()
	{
		string html = await RenderAsync<Sparkline>(new() { ["Values"] = new List<double> { 5 }, ["Max"] = 10.0, ["Slots"] = 40 });

		Assert.Contains("moka-sparkline", html, StringComparison.Ordinal);
		Assert.DoesNotContain("moka-sparkline-line", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters)
		where TComponent : Microsoft.AspNetCore.Components.IComponent =>
		StaticRender.RenderAsync<TComponent>(_ => { }, parameters).WaitAsync(Wait, TestContext.Current.CancellationToken);
}
